using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PharmaFlow.Documents.Api.Services;
using PharmaFlow.Identity.Api.Services;
using Xunit;

namespace PharmaFlow.UnitTests;

public class DocumentAndSecurityTests
{
    [Fact]
    public async Task ValidateFileSignature_RejectsFakePdfWithWrongMagicBytes()
    {
        var configMock = new Mock<IConfiguration>();
        var envMock = new Mock<IWebHostEnvironment>();
        var loggerMock = new Mock<ILogger<DocumentStorageService>>();

        var service = new DocumentStorageService(configMock.Object, envMock.Object, loggerMock.Object);

        // Fake PDF with plain text bytes instead of %PDF header
        var fakeBytes = Encoding.UTF8.GetBytes("This is a fake text file disguised as a pdf document.");
        using var stream = new MemoryStream(fakeBytes);

        var (isValid, error, _) = await service.ValidateFileSignatureAsync(stream, "prescription.pdf");

        Assert.False(isValid);
        Assert.Contains("does not match valid PDF header signature", error);
    }

    [Fact]
    public async Task ValidateFileSignature_AcceptsValidPdfHeader()
    {
        var configMock = new Mock<IConfiguration>();
        var envMock = new Mock<IWebHostEnvironment>();
        var loggerMock = new Mock<ILogger<DocumentStorageService>>();

        var service = new DocumentStorageService(configMock.Object, envMock.Object, loggerMock.Object);

        // Valid PDF header: %PDF-1.7
        var validPdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7 header content here for testing valid signature");
        using var stream = new MemoryStream(validPdfBytes);

        var (isValid, error, contentType) = await service.ValidateFileSignatureAsync(stream, "prescription.pdf");

        Assert.True(isValid);
        Assert.Null(error);
        Assert.Equal("application/pdf", contentType);
    }

    [Fact]
    public async Task MalwareScan_FailsClosedInProduction_WhenScannerNotConfigured()
    {
        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["Security:MalwareScannerEndpoint"]).Returns((string?)null); // Missing scanner

        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Production");

        var loggerMock = new Mock<ILogger<DocumentStorageService>>();

        var service = new DocumentStorageService(configMock.Object, envMock.Object, loggerMock.Object);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("sample content"));

        var status = await service.ScanForMalwareAsync(stream, "prescription.pdf");

        Assert.Equal("Failed", status); // Fails closed!
    }

    [Fact]
    public void PasswordHasher_VerifiesCorrectPasswordAndRejectsWrong()
    {
        var hasher = new PasswordHasher();
        var password = "SecurePharmaPassword2026!";

        var hash = hasher.HashPassword(password);

        Assert.True(hasher.VerifyPassword(password, hash));
        Assert.False(hasher.VerifyPassword("WrongPassword123!", hash));
    }
}
