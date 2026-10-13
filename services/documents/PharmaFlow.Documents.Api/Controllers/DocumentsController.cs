using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Contracts.Common;
using PharmaFlow.Documents.Api.Data;
using PharmaFlow.Documents.Api.Domain;
using PharmaFlow.Documents.Api.Services;

namespace PharmaFlow.Documents.Api.Controllers;

public record ReviewPrescriptionRequest(
    string Status, // "Approved", "ClarificationRequested", "Rejected"
    string? Notes,
    List<ApprovedItemInput>? ApprovedItems);

public record ApprovedItemInput(
    Guid ProductId,
    string ProductName,
    int MaxDispenseQuantity);

[ApiController]
[Route("api/documents")]
[Authorize]
public class DocumentsController : ControllerBase
{
    private readonly DocumentsDbContext _db;
    private readonly IDocumentStorageService _storage;

    public DocumentsController(DocumentsDbContext db, IDocumentStorageService storage)
    {
        _db = db;
        _storage = storage;
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadDocument([FromForm] IFormFile file, [FromForm] string purpose = "Prescription")
    {
        var userIdStr = User.FindFirstValue(ClaimConstants.UserId);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        if (file == null || file.Length == 0)
        {
            return BadRequest(ApiResponse<Guid>.Fail("No file provided."));
        }

        using var stream = file.OpenReadStream();

        // 1. Validate File Signature (Magic Bytes)
        var (isValid, sigError, contentType) = await _storage.ValidateFileSignatureAsync(stream, file.FileName);
        if (!isValid)
        {
            return BadRequest(ApiResponse<Guid>.Fail(sigError ?? "File signature validation failed."));
        }

        // 2. Scan for Malware (Dev vs Prod fail-closed check)
        var scanResult = await _storage.ScanForMalwareAsync(stream, file.FileName);
        if (scanResult != "Clean")
        {
            return StatusCode(StatusCodes.Status422UnprocessableEntity,
                ApiResponse<Guid>.Fail($"File rejected: Malware scan status is '{scanResult}'."));
        }

        // 3. Store Blob & Compute Hash
        var (blobName, sha256) = await _storage.StoreBlobAsync(stream, file.FileName, contentType);

        var doc = new DocumentMetadata
        {
            OriginalFileName = file.FileName,
            StoredBlobName = blobName,
            ContentType = contentType,
            FileSizeBytes = file.Length,
            Sha256Hash = sha256,
            Purpose = purpose,
            OwnerUserId = userId,
            ScanStatus = scanResult,
            UploadedAtUtc = DateTime.UtcNow,
            ReviewStatus = purpose == "Prescription" ? "PendingReview" : "N/A"
        };

        _db.Documents.Add(doc);
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            DocumentId = doc.Id,
            doc.OriginalFileName,
            doc.FileSizeBytes,
            doc.Sha256Hash,
            doc.ScanStatus,
            doc.ReviewStatus
        }, "Document uploaded and verified successfully."));
    }

    [HttpGet("{id}/metadata")]
    public async Task<IActionResult> GetMetadata(Guid id)
    {
        var doc = await _db.Documents
            .Include(d => d.ApprovedItems)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (doc == null) return NotFound(ApiResponse<DocumentMetadata>.Fail("Document not found."));

        return Ok(ApiResponse<DocumentMetadata>.Ok(doc));
    }

    [HttpGet("{id}/download")]
    public async Task<IActionResult> DownloadDocument(Guid id)
    {
        var userIdStr = User.FindFirstValue(ClaimConstants.UserId);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id);
        if (doc == null) return NotFound("Document not found.");

        if (doc.ScanStatus != "Clean")
        {
            return Forbid("Access denied: Document is not verified clean.");
        }

        var stream = await _storage.RetrieveBlobAsync(doc.StoredBlobName);
        if (stream == null) return NotFound("File content not found in storage.");

        // Audit Trail Record
        _db.DocumentAccessAudits.Add(new DocumentAccessAudit
        {
            DocumentId = doc.Id,
            AccessedByUserId = userId,
            AccessType = "Download",
            TimestampUtc = DateTime.UtcNow,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
        });
        await _db.SaveChangesAsync();

        return File(stream, doc.ContentType, doc.OriginalFileName);
    }

    [HttpGet("prescriptions/pending")]
    public async Task<IActionResult> GetPendingPrescriptions()
    {
        var list = await _db.Documents
            .Where(d => d.Purpose == "Prescription" && d.ReviewStatus == "PendingReview")
            .OrderByDescending(d => d.UploadedAtUtc)
            .ToListAsync();

        return Ok(ApiResponse<List<DocumentMetadata>>.Ok(list));
    }

    [HttpPost("{id}/review")]
    public async Task<IActionResult> ReviewPrescription(Guid id, [FromBody] ReviewPrescriptionRequest req)
    {
        var userIdStr = User.FindFirstValue(ClaimConstants.UserId);
        if (!Guid.TryParse(userIdStr, out var pharmacistId)) return Unauthorized();

        var doc = await _db.Documents
            .Include(d => d.ApprovedItems)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (doc == null) return NotFound(ApiResponse<bool>.Fail("Prescription document not found."));

        doc.ReviewStatus = req.Status;
        doc.ReviewedByPharmacistId = pharmacistId;
        doc.ReviewedAtUtc = DateTime.UtcNow;
        doc.PharmacistNotes = req.Notes;

        if (req.Status == "Approved" && req.ApprovedItems != null)
        {
            doc.ApprovedItems.Clear();
            foreach (var item in req.ApprovedItems)
            {
                doc.ApprovedItems.Add(new PrescriptionApprovedItem
                {
                    DocumentId = doc.Id,
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    MaxDispenseQuantity = item.MaxDispenseQuantity,
                    DispensedQuantity = 0
                });
            }
        }

        // Audit log
        _db.DocumentAccessAudits.Add(new DocumentAccessAudit
        {
            DocumentId = doc.Id,
            AccessedByUserId = pharmacistId,
            AccessType = "Review",
            TimestampUtc = DateTime.UtcNow,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true, $"Prescription marked as {req.Status}."));
    }
}
