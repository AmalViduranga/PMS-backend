using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PharmaFlow.Documents.Api.Data;
using PharmaFlow.Documents.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "PharmaFlow Documents & Prescriptions API", Version = "v1" });
});

var connectionString = builder.Configuration.GetConnectionString("DocumentsDb")
    ?? "Host=localhost;Port=5432;Database=pharmaflow_documents;Username=documents_user;Password=DocumentsSecretPass123!;";

builder.Services.AddDbContext<DocumentsDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

builder.Services.AddScoped<IDocumentStorageService, DocumentStorageService>();

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
builder.Services.AddHealthChecks();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllLocal", p => p.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod().AllowCredentials());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "PharmaFlow Documents API v1"));
}

app.UseCors("AllowAllLocal");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
        if (db.Database.CanConnect()) db.Database.Migrate();
    }
    catch { }
}

app.Run();
