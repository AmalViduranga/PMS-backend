using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PharmaFlow.Contracts.Common;
using PharmaFlow.Gateway.Middleware;

namespace PharmaFlow.Gateway.Controllers;

[ApiController]
[Route("api/bff")]
public class BffController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBffSessionStore _sessionStore;
    private readonly IConfiguration _config;
    private readonly ILogger<BffController> _logger;

    public BffController(
        IHttpClientFactory httpClientFactory,
        IBffSessionStore sessionStore,
        IConfiguration config,
        ILogger<BffController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _sessionStore = sessionStore;
        _config = config;
        _logger = logger;
    }

    [HttpGet("csrf")]
    public IActionResult GetCsrf()
    {
        var csrfToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        Response.Cookies.Append(BffAuthMiddleware.CsrfCookieName, csrfToken, new CookieOptions
        {
            HttpOnly = false, // Accessible by Angular HttpClient XSRF module
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Secure = false // Allowed on http localhost in dev
        });

        return Ok(ApiResponse<object>.Ok(new { csrfToken }));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] JsonElement loginPayload)
    {
        var client = _httpClientFactory.CreateClient("IdentityService");
        var content = new StringContent(loginPayload.GetRawText(), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/auth/login", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            return StatusCode((int)response.StatusCode, Content(responseBody, "application/json"));
        }

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;
        if (!root.TryGetProperty("data", out var dataElem))
        {
            return StatusCode((int)response.StatusCode, Content(responseBody, "application/json"));
        }

        var accessToken = dataElem.GetProperty("accessToken").GetString() ?? "";
        var refreshToken = dataElem.GetProperty("refreshToken").GetString() ?? "";
        var expiresAt = dataElem.GetProperty("expiresAtUtc").GetDateTime();
        var userElem = dataElem.GetProperty("user").Clone();
        var actorType = userElem.TryGetProperty("actorType", out var atProp) ? atProp.GetString() ?? "staff" : "staff";

        // Create secure BFF session
        var sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var csrfToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        var session = new BffSession
        {
            SessionId = sessionId,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ActorType = actorType,
            CsrfToken = csrfToken,
            ExpiresAtUtc = expiresAt
        };
        _sessionStore.SaveSession(session);

        // Set HttpOnly session cookie
        var cookieName = actorType == ClaimConstants.ActorTypeCustomer
            ? BffAuthMiddleware.CustomerCookieName
            : BffAuthMiddleware.StaffCookieName;

        Response.Cookies.Append(cookieName, sessionId, new CookieOptions
        {
            HttpOnly = true, // Preserved HttpOnly cookie per Section 7
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            Secure = false
        });

        // Set CSRF token cookie readable by Angular
        Response.Cookies.Append(BffAuthMiddleware.CsrfCookieName, csrfToken, new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            Secure = false
        });

        // Return user info only (Token is preserved in HttpOnly cookie, NEVER exposed to localStorage)
        return Ok(ApiResponse<object>.Ok(new
        {
            user = userElem,
            csrfToken,
            message = "Authentication successful. Session established via secure HttpOnly cookie."
        }));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        string? staffSessionId = Request.Cookies[BffAuthMiddleware.StaffCookieName];
        string? customerSessionId = Request.Cookies[BffAuthMiddleware.CustomerCookieName];

        if (!string.IsNullOrEmpty(staffSessionId))
        {
            _sessionStore.RemoveSession(staffSessionId);
            Response.Cookies.Delete(BffAuthMiddleware.StaffCookieName);
        }

        if (!string.IsNullOrEmpty(customerSessionId))
        {
            _sessionStore.RemoveSession(customerSessionId);
            Response.Cookies.Delete(BffAuthMiddleware.CustomerCookieName);
        }

        Response.Cookies.Delete(BffAuthMiddleware.CsrfCookieName);

        return Ok(ApiResponse<bool>.Ok(true, "Logged out successfully."));
    }

    [HttpGet("user")]
    public async Task<IActionResult> GetCurrentUser()
    {
        string? sessionId = Request.Cookies[BffAuthMiddleware.StaffCookieName] ?? Request.Cookies[BffAuthMiddleware.CustomerCookieName];
        if (string.IsNullOrEmpty(sessionId))
        {
            return Unauthorized(ApiResponse<object>.Fail("No active session cookie found."));
        }

        var session = _sessionStore.GetSession(sessionId);
        if (session == null || session.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return Unauthorized(ApiResponse<object>.Fail("Session expired."));
        }

        var client = _httpClientFactory.CreateClient("IdentityService");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

        var response = await client.GetAsync("/api/auth/me");
        var responseBody = await response.Content.ReadAsStringAsync();

        return StatusCode((int)response.StatusCode, Content(responseBody, "application/json"));
    }
}
