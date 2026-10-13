using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using PharmaFlow.Contracts.Common;

namespace PharmaFlow.Gateway.Middleware;

public class BffSession
{
    public string SessionId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string ActorType { get; set; } = ClaimConstants.ActorTypeStaff;
    public string CsrfToken { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}

public interface IBffSessionStore
{
    void SaveSession(BffSession session);
    BffSession? GetSession(string sessionId);
    void RemoveSession(string sessionId);
}

public class MemoryBffSessionStore : IBffSessionStore
{
    private readonly ConcurrentDictionary<string, BffSession> _sessions = new();

    public void SaveSession(BffSession session) => _sessions[session.SessionId] = session;
    public BffSession? GetSession(string sessionId) => _sessions.TryGetValue(sessionId, out var s) ? s : null;
    public void RemoveSession(string sessionId) => _sessions.TryRemove(sessionId, out _);
}

public class BffAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IBffSessionStore _sessionStore;
    private readonly ILogger<BffAuthMiddleware> _logger;

    public const string StaffCookieName = "pharmaflow_staff_session";
    public const string CustomerCookieName = "pharmaflow_customer_session";
    public const string CsrfCookieName = "XSRF-TOKEN";
    public const string CsrfHeaderName = "X-XSRF-TOKEN";
    public const string AltCsrfHeaderName = "X-CSRF-TOKEN";

    public BffAuthMiddleware(RequestDelegate next, IBffSessionStore sessionStore, ILogger<BffAuthMiddleware> logger)
    {
        _next = next;
        _sessionStore = sessionStore;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 1. Check for BFF session cookie if Authorization header is not explicitly set
        if (!context.Request.Headers.ContainsKey("Authorization"))
        {
            string? sessionId = context.Request.Cookies[StaffCookieName] ?? context.Request.Cookies[CustomerCookieName];

            if (!string.IsNullOrEmpty(sessionId))
            {
                var session = _sessionStore.GetSession(sessionId);
                if (session != null && session.ExpiresAtUtc > DateTime.UtcNow)
                {
                    // 2. Validate CSRF for State-Changing Methods
                    var method = context.Request.Method;
                    if (HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsDelete(method) || HttpMethods.IsPatch(method))
                    {
                        // Allow login & bootstrap without existing CSRF check
                        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
                        if (!path.EndsWith("/login") && !path.EndsWith("/bootstrap-admin"))
                        {
                            var headerCsrf = context.Request.Headers[CsrfHeaderName].FirstOrDefault()
                                          ?? context.Request.Headers[AltCsrfHeaderName].FirstOrDefault();

                            if (string.IsNullOrEmpty(headerCsrf) || headerCsrf != session.CsrfToken)
                            {
                                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                                context.Response.ContentType = "application/json";
                                await context.Response.WriteAsync(JsonSerializer.Serialize(ApiResponse<bool>.Fail("CSRF validation failed.")));
                                return;
                            }
                        }
                    }

                    // 3. Attach Bearer Token to downstream request
                    context.Request.Headers["Authorization"] = $"Bearer {session.AccessToken}";
                }
            }
        }

        await _next(context);
    }
}
