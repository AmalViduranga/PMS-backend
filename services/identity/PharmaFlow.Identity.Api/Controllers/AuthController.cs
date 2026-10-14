using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Contracts.Common;
using PharmaFlow.Identity.Api.Data;
using PharmaFlow.Identity.Api.Domain;
using PharmaFlow.Identity.Api.DTOs;
using PharmaFlow.Identity.Api.Services;

namespace PharmaFlow.Identity.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IdentityDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokenService;
    private readonly IBootstrapService _bootstrapService;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IdentityDbContext db,
        IPasswordHasher hasher,
        ITokenService tokenService,
        IBootstrapService bootstrapService,
        IEmailSender emailSender,
        ILogger<AuthController> logger)
    {
        _db = db;
        _hasher = hasher;
        _tokenService = tokenService;
        _bootstrapService = bootstrapService;
        _emailSender = emailSender;
        _logger = logger;
    }

    [HttpGet("bootstrap-status")]
    public async Task<IActionResult> GetBootstrapStatus()
    {
        var isBootstrapped = await _bootstrapService.IsBootstrappedAsync();
        return Ok(ApiResponse<object>.Ok(new { isBootstrapped }));
    }

    [HttpPost("bootstrap-admin")]
    public async Task<IActionResult> BootstrapAdmin([FromBody] BootstrapAdminRequest req)
    {
        var result = await _bootstrapService.BootstrapAdminAsync(req.Email, req.Password, req.FullName, req.BootstrapToken);
        if (!result.Success)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role).ThenInclude(r => r.RolePermissions)
            .Include(u => u.BranchAssignments)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == req.Email.Trim().ToLower());

        if (user == null)
        {
            return Unauthorized(ApiResponse<LoginResponse>.Fail("Invalid email or password."));
        }

        if (!user.IsActive)
        {
            return Unauthorized(ApiResponse<LoginResponse>.Fail("Account is inactive. Please contact your administrator."));
        }

        // Enforce staff access boundary: Customer accounts are strictly denied staff portal access
        if ((string.IsNullOrWhiteSpace(req.Portal) || req.Portal.Equals("staff", StringComparison.OrdinalIgnoreCase)) &&
            user.ActorType != ClaimConstants.ActorTypeStaff)
        {
            return Unauthorized(ApiResponse<LoginResponse>.Fail("Customer accounts cannot access the staff portal."));
        }

        if (user.IsLockedOut && user.LockoutEndUtc.HasValue && user.LockoutEndUtc.Value > DateTime.UtcNow)
        {
            var remaining = (int)Math.Ceiling((user.LockoutEndUtc.Value - DateTime.UtcNow).TotalMinutes);
            return StatusCode(StatusCodes.Status423Locked, ApiResponse<LoginResponse>.Fail($"Account is temporarily locked. Try again in {remaining} minute(s)."));
        }

        var isPasswordValid = _hasher.VerifyPassword(req.Password, user.PasswordHash);
        if (!isPasswordValid)
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= 5)
            {
                user.IsLockedOut = true;
                user.LockoutEndUtc = DateTime.UtcNow.AddMinutes(15);
                _logger.LogWarning("Account locked out due to failed attempts: {Email}", user.Email);
            }
            await _db.SaveChangesAsync();
            return Unauthorized(ApiResponse<LoginResponse>.Fail("Invalid email or password."));
        }

        // Reset failed login attempts on successful login
        user.FailedLoginAttempts = 0;
        user.IsLockedOut = false;
        user.LockoutEndUtc = null;

        var roles = user.UserRoles.Select(ur => ur.Role.Name).Distinct().ToList();
        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission)
            .Distinct()
            .ToList();

        var primaryBranchId = user.BranchAssignments.FirstOrDefault(b => b.IsPrimary)?.BranchId
            ?? user.BranchAssignments.FirstOrDefault()?.BranchId;

        var (accessToken, expiresAtUtc) = _tokenService.GenerateAccessToken(user, roles, permissions, primaryBranchId);
        var refreshToken = _tokenService.GenerateRefreshToken(user.Id);

        _db.RefreshTokens.Add(refreshToken);
        await _db.SaveChangesAsync();

        var userDto = new UserDto(
            user.Id,
            user.Email,
            user.FullName,
            user.ActorType,
            roles,
            permissions,
            primaryBranchId);

        return Ok(ApiResponse<LoginResponse>.Ok(new LoginResponse(accessToken, expiresAtUtc, refreshToken.Token, userDto)));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest req)
    {
        var existingToken = await _db.RefreshTokens
            .Include(rt => rt.User).ThenInclude(u => u.UserRoles).ThenInclude(ur => ur.Role).ThenInclude(r => r.RolePermissions)
            .Include(rt => rt.User).ThenInclude(u => u.BranchAssignments)
            .FirstOrDefaultAsync(rt => rt.Token == req.RefreshToken);

        if (existingToken == null || !existingToken.IsActive)
        {
            return Unauthorized(ApiResponse<LoginResponse>.Fail("Invalid or expired refresh token."));
        }

        var user = existingToken.User;
        if (!user.IsActive)
        {
            return Unauthorized(ApiResponse<LoginResponse>.Fail("Account is inactive."));
        }

        // Revoke the old token (token rotation)
        existingToken.RevokedAtUtc = DateTime.UtcNow;

        var roles = user.UserRoles.Select(ur => ur.Role.Name).Distinct().ToList();
        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission)
            .Distinct()
            .ToList();

        var primaryBranchId = user.BranchAssignments.FirstOrDefault(b => b.IsPrimary)?.BranchId
            ?? user.BranchAssignments.FirstOrDefault()?.BranchId;

        var (newAccessToken, expiresAtUtc) = _tokenService.GenerateAccessToken(user, roles, permissions, primaryBranchId);
        var newRefreshToken = _tokenService.GenerateRefreshToken(user.Id);

        existingToken.ReplacedByToken = newRefreshToken.Token;
        _db.RefreshTokens.Add(newRefreshToken);
        await _db.SaveChangesAsync();

        var userDto = new UserDto(
            user.Id,
            user.Email,
            user.FullName,
            user.ActorType,
            roles,
            permissions,
            primaryBranchId);

        return Ok(ApiResponse<LoginResponse>.Ok(new LoginResponse(newAccessToken, expiresAtUtc, newRefreshToken.Token, userDto)));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest req)
    {
        var token = await _db.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == req.RefreshToken);
        if (token != null)
        {
            token.RevokedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return Ok(ApiResponse<bool>.Ok(true, "Logged out successfully."));
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email))
        {
            return Ok(ApiResponse<bool>.Ok(true, "If an account with that email exists, password reset instructions have been sent."));
        }

        var normalizedEmail = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

        if (user != null && user.IsActive)
        {
            // Rate-limiting check: At most one token request every 2 minutes per account
            var hasRecentToken = await _db.PasswordResetTokens
                .AnyAsync(t => t.UserId == user.Id && t.CreatedAtUtc > DateTime.UtcNow.AddMinutes(-2));

            if (!hasRecentToken)
            {
                // Invalidate any previously unconsumed active tokens for this user
                var previousTokens = await _db.PasswordResetTokens
                    .Where(t => t.UserId == user.Id && t.UsedAtUtc == null && t.ExpiresAtUtc > DateTime.UtcNow)
                    .ToListAsync();
                foreach (var pt in previousTokens)
                {
                    pt.UsedAtUtc = DateTime.UtcNow;
                }

                // Cryptographically secure 256-bit single-use token
                var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                var resetToken = new PasswordResetToken
                {
                    UserId = user.Id,
                    TokenHash = _hasher.HashPassword(rawToken),
                    ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
                };

                _db.PasswordResetTokens.Add(resetToken);
                await _db.SaveChangesAsync();

                // Send email to local MailDev (port 1025) or configured SMTP server. Never log raw tokens.
                await _emailSender.SendPasswordResetEmailAsync(user.Email, user.FullName, rawToken);
            }
        }

        // Generic response that does not reveal whether an account exists
        return Ok(ApiResponse<bool>.Ok(true, "If an account with that email exists, password reset instructions have been sent."));
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.ResetToken) || string.IsNullOrWhiteSpace(req.NewPassword))
        {
            return BadRequest(ApiResponse<bool>.Fail("Invalid password reset request."));
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == req.Email.Trim().ToLower());
        if (user == null || !user.IsActive)
        {
            return BadRequest(ApiResponse<bool>.Fail("Invalid or expired password reset token."));
        }

        // Backend password validation
        if (req.NewPassword.Length < 8 ||
            !req.NewPassword.Any(char.IsUpper) ||
            !req.NewPassword.Any(char.IsLower) ||
            !req.NewPassword.Any(char.IsDigit))
        {
            return BadRequest(ApiResponse<bool>.Fail("Password must be at least 8 characters and contain uppercase, lowercase, and numeric characters."));
        }

        var activeTokens = await _db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.UsedAtUtc == null && t.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync();

        PasswordResetToken? matchedToken = null;
        foreach (var t in activeTokens)
        {
            if (_hasher.VerifyPassword(req.ResetToken, t.TokenHash))
            {
                matchedToken = t;
                break;
            }
        }

        if (matchedToken == null)
        {
            return BadRequest(ApiResponse<bool>.Fail("Invalid or expired password reset token."));
        }

        // Atomic token consumption
        matchedToken.UsedAtUtc = DateTime.UtcNow;

        // Invalidate all remaining tokens for this user
        foreach (var t in activeTokens.Where(t => t.Id != matchedToken.Id))
        {
            t.UsedAtUtc = DateTime.UtcNow;
        }

        // Update user credentials & clear lockouts
        user.PasswordHash = _hasher.HashPassword(req.NewPassword);
        user.FailedLoginAttempts = 0;
        user.IsLockedOut = false;
        user.LockoutEndUtc = null;
        user.UpdatedAtUtc = DateTime.UtcNow;

        // Invalidate all existing refresh sessions for this user
        var activeRefreshTokens = await _db.RefreshTokens
            .Where(rt => rt.UserId == user.Id && rt.RevokedAtUtc == null)
            .ToListAsync();
        foreach (var rt in activeRefreshTokens)
        {
            rt.RevokedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true, "Password successfully reset. You may now log in with your new password."));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userIdClaim = User.FindFirstValue(ClaimConstants.UserId);
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(ApiResponse<UserDto>.Fail("Unauthorized session."));
        }

        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role).ThenInclude(r => r.RolePermissions)
            .Include(u => u.BranchAssignments)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null || !user.IsActive)
        {
            return Unauthorized(ApiResponse<UserDto>.Fail("User not found or inactive."));
        }

        var roles = user.UserRoles.Select(ur => ur.Role.Name).Distinct().ToList();
        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission)
            .Distinct()
            .ToList();

        var primaryBranchId = user.BranchAssignments.FirstOrDefault(b => b.IsPrimary)?.BranchId
            ?? user.BranchAssignments.FirstOrDefault()?.BranchId;

        var userDto = new UserDto(
            user.Id,
            user.Email,
            user.FullName,
            user.ActorType,
            roles,
            permissions,
            primaryBranchId);

        return Ok(ApiResponse<UserDto>.Ok(userDto));
    }
}
