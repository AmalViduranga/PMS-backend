using Microsoft.OpenApi.Models;
using PharmaFlow.Gateway.Middleware;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "PharmaFlow Gateway & BFF", Version = "v1" });
});

// BFF Session Store & HttpClients
builder.Services.AddSingleton<IBffSessionStore, MemoryBffSessionStore>();

builder.Services.AddHttpClient("IdentityService", client =>
{
    var url = builder.Configuration["Services:IdentityUrl"] ?? "http://localhost:5001";
    client.BaseAddress = new Uri(url);
});

builder.Services.AddHttpClient("OperationsService", client =>
{
    var url = builder.Configuration["Services:OperationsUrl"] ?? "http://localhost:5002";
    client.BaseAddress = new Uri(url);
});

// Reverse Proxy (YARP)
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(builderContext =>
    {
        builderContext.AddRequestTransform(transformContext =>
        {
            if (transformContext.HttpContext.Request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                transformContext.ProxyRequest.Headers.Remove("Authorization");
                transformContext.ProxyRequest.Headers.TryAddWithoutValidation("Authorization", authHeader.ToArray());
            }
            return ValueTask.CompletedTask;
        });
    });

// Health checks
builder.Services.AddHealthChecks();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:4200", "http://localhost:4201")
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
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "PharmaFlow Gateway v1");
    });
}

app.UseCors("AllowFrontend");

// BFF Authentication Middleware
app.UseMiddleware<BffAuthMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health");

// Forward all other configured API routes through YARP
app.MapReverseProxy();

app.Run();
