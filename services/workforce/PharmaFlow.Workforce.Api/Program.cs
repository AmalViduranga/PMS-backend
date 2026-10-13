using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PharmaFlow.Workforce.Api.Data;
using PharmaFlow.Workforce.Api.Domain;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "PharmaFlow Workforce API", Version = "v1" });
});

var connectionString = builder.Configuration.GetConnectionString("WorkforceDb")
    ?? "Host=localhost;Port=5432;Database=pharmaflow_workforce;Username=workforce_user;Password=WorkforceSecretPass123!;";

builder.Services.AddDbContext<WorkforceDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

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
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "PharmaFlow Workforce API v1"));
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
        var db = scope.ServiceProvider.GetRequiredService<WorkforceDbContext>();
        if (db.Database.CanConnect())
        {
            db.Database.Migrate();

            // Seed default leave types
            if (!db.LeaveTypes.Any())
            {
                db.LeaveTypes.AddRange(
                    new LeaveType { Name = "Annual", AnnualAllowanceDays = 14 },
                    new LeaveType { Name = "Casual", AnnualAllowanceDays = 7 },
                    new LeaveType { Name = "Medical", AnnualAllowanceDays = 14 }
                );
                db.SaveChanges();
            }
        }
    }
    catch { }
}

app.Run();
