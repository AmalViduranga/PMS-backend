using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PharmaFlow.Identity.Api.Data;
using PharmaFlow.Identity.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add Controllers & Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "PharmaFlow Identity & Access API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// Database
var connectionString = builder.Configuration.GetConnectionString("IdentityDb")
    ?? "Host=localhost;Port=5432;Database=pharmaflow_identity;Username=identity_user;Password=IdentitySecretPass123!;";

builder.Services.AddDbContext<IdentityDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

// Auth Services
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IBootstrapService, BootstrapService>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

// JWT Authentication
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "PharmaFlow_Ultra_Secure_Dev_Signing_Key_2026_AtLeast32Bytes!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "PharmaFlow.Identity";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "PharmaFlow.Services";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization();

// Health checks
builder.Services.AddHealthChecks();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllLocal", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Local Development Admin Recovery CLI (Non-HTTP, explicit interactive command)
if (args.Contains("--recover-admin"))
{
    if (!app.Environment.IsDevelopment())
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[ERROR] The admin recovery command is only permitted in the Development environment.");
        Console.ResetColor();
        return;
    }

    using var recoveryScope = app.Services.CreateScope();
    var db = recoveryScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    var hasher = recoveryScope.ServiceProvider.GetRequiredService<IPasswordHasher>();

    Console.WriteLine("==================================================");
    Console.WriteLine(" PharmaFlow - Local Development Admin Recovery CLI");
    Console.WriteLine("==================================================");

    var superAdmins = await db.Users
        .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
        .Where(u => u.UserRoles.Any(ur => ur.Role.Name == "SuperAdmin"))
        .ToListAsync();

    if (superAdmins.Count == 0)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[ERROR] No existing SuperAdmin accounts found in the database.");
        Console.ResetColor();
        return;
    }

    Console.WriteLine("\nExisting SuperAdmin accounts:");
    for (int i = 0; i < superAdmins.Count; i++)
    {
        var sa = superAdmins[i];
        Console.WriteLine($"  [{i + 1}] Email: {sa.Email} | Name: {sa.FullName} | Active: {sa.IsActive} | Locked: {sa.IsLockedOut}");
    }

    int selectedIndex = 0;
    if (superAdmins.Count > 1)
    {
        Console.Write($"\nSelect SuperAdmin account [1-{superAdmins.Count}]: ");
        var choice = Console.ReadLine();
        if (!int.TryParse(choice, out selectedIndex) || selectedIndex < 1 || selectedIndex > superAdmins.Count)
        {
            Console.WriteLine("[ERROR] Invalid selection. Aborting.");
            return;
        }
        selectedIndex--;
    }
    else
    {
        selectedIndex = 0;
    }

    var selectedAdmin = superAdmins[selectedIndex];
    Console.WriteLine($"\nRecovering password for: {selectedAdmin.Email}");

    string ReadHiddenPassword(string prompt)
    {
        Console.Write(prompt);
        var sb = new StringBuilder();
        while (true)
        {
            var keyInfo = Console.ReadKey(intercept: true);
            if (keyInfo.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            if (keyInfo.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0)
                {
                    sb.Remove(sb.Length - 1, 1);
                    Console.Write("\b \b");
                }
            }
            else if (!char.IsControl(keyInfo.KeyChar))
            {
                sb.Append(keyInfo.KeyChar);
                Console.Write("*");
            }
        }
        return sb.ToString();
    }

    string newPass = ReadHiddenPassword("Enter new password (min 8 chars): ");
    if (string.IsNullOrWhiteSpace(newPass) || newPass.Length < 8)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[ERROR] Password must be at least 8 characters long. Aborting.");
        Console.ResetColor();
        return;
    }

    string confirmPass = ReadHiddenPassword("Confirm new password: ");
    if (newPass != confirmPass)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[ERROR] Passwords do not match. Aborting.");
        Console.ResetColor();
        return;
    }

    // Update password hash using application's IPasswordHasher
    selectedAdmin.PasswordHash = hasher.HashPassword(newPass);
    selectedAdmin.FailedLoginAttempts = 0;
    selectedAdmin.IsLockedOut = false;
    selectedAdmin.LockoutEndUtc = null;
    selectedAdmin.UpdatedAtUtc = DateTime.UtcNow;

    // Invalidate existing sessions using the application's supported mechanism
    var activeTokens = await db.RefreshTokens
        .Where(rt => rt.UserId == selectedAdmin.Id && rt.RevokedAtUtc == null)
        .ToListAsync();
    foreach (var rt in activeTokens)
    {
        rt.RevokedAtUtc = DateTime.UtcNow;
    }

    await db.SaveChangesAsync();

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("\n[SUCCESS] Password updated successfully!");
    Console.WriteLine($"  - Account: {selectedAdmin.Email}");
    Console.WriteLine($"  - Sessions Revoked: {activeTokens.Count} active refresh token(s) invalidated.");
    Console.WriteLine("  - Account Lockout: Cleared.");
    Console.WriteLine("  - Roles & Bootstrap State: Preserved.");
    Console.ResetColor();
    Console.WriteLine("\nYou can now log in at: http://localhost:4200/login\n");
    return;
}


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "PharmaFlow Identity API v1");
    });
}

app.UseCors("AllowAllLocal");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

// Safe Startup Migration / Role Seeding Check
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        // Only attempt automatic migration if can connect
        if (db.Database.CanConnect())
        {
            db.Database.Migrate();
            var bootstrapSvc = scope.ServiceProvider.GetRequiredService<IBootstrapService>();
            bootstrapSvc.SeedDefaultRolesAndPermissionsAsync().GetAwaiter().GetResult();
            var isBootstrapped = bootstrapSvc.IsBootstrappedAsync().GetAwaiter().GetResult();
            if (!isBootstrapped)
            {
                var token = bootstrapSvc.GetOrGenerateBootstrapTokenAsync().GetAwaiter().GetResult();
                logger.LogWarning("System is not yet bootstrapped. Bootstrap token: {Token}", token);
            }
        }
        else
        {
            logger.LogWarning("PostgreSQL database not yet reachable. Skipping startup migration check until infrastructure is available.");
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not run startup DB migration check; will rely on manual/containerized migrations.");
    }
}

app.Run();
app.Run();
