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
