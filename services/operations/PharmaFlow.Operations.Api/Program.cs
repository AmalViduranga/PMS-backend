using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PharmaFlow.Operations.Api.Data;
using PharmaFlow.Operations.Api.Domain;
using PharmaFlow.Operations.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Controllers & Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "PharmaFlow Pharmacy Operations API", Version = "v1" });
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
var connectionString = builder.Configuration.GetConnectionString("OperationsDb")
    ?? "Host=localhost;Port=5432;Database=pharmaflow_operations;Username=operations_user;Password=OperationsSecretPass123!;";

builder.Services.AddDbContext<OperationsDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

// Domain Services
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IPosService, PosService>();
builder.Services.AddScoped<IReturnService, ReturnService>();

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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "PharmaFlow Operations API v1");
    });
}

app.UseCors("AllowAllLocal");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

// Safe Startup Migration & Default Branch/Org Seeding
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        if (db.Database.CanConnect())
        {
            db.Database.Migrate();

            // Seed default Organization & Main Branch if not existing
            var org = db.Organizations.FirstOrDefault();
            if (org == null)
            {
                org = new Organization
                {
                    Name = "PharmaFlow Healthcare",
                    DefaultCurrency = "LKR",
                    DefaultTimezone = "Asia/Colombo"
                };
                db.Organizations.Add(org);
                db.SaveChanges();
            }

            var branch = db.Branches.FirstOrDefault();
            if (branch == null)
            {
                branch = new Branch
                {
                    OrganizationId = org.Id,
                    Code = "MAIN",
                    Name = "PharmaFlow Central Pharmacy",
                    Address = "100 Galle Road, Colombo 03, Sri Lanka",
                    Phone = "+94 11 234 5678",
                    Email = "central@pharmaflow.local",
                    Timezone = "Asia/Colombo",
                    Currency = "LKR",
                    IsActive = true
                };
                db.Branches.Add(branch);
                db.SaveChanges();

                var register = new Register
                {
                    BranchId = branch.Id,
                    Code = "REG-01",
                    Name = "Main Counter Register 1",
                    HardwareIdentifier = "POS-TERMINAL-01",
                    IsActive = true
                };
                db.Registers.Add(register);
                db.SaveChanges();
            }
        }
        else
        {
            logger.LogWarning("Operations database not reachable on startup; skipping migration check.");
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not run operations DB startup migration check.");
    }
}

app.Run();
