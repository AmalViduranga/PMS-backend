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
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IdentityDbContext _db;
    private readonly IPasswordHasher _hasher;

    public UsersController(IdentityDbContext db, IPasswordHasher hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    [HttpGet]
    public async Task<IActionResult> GetUsers([FromQuery] int pageIndex = 0, [FromQuery] int pageSize = 20, [FromQuery] string? search = null)
    {
        // Enforce permission
        if (!User.Claims.Any(c => c.Type == ClaimConstants.Permission && (c.Value == Permissions.AdminUsers || c.Value == Permissions.AdminBootstrap)))
        {
            return Forbid();
        }

        var query = _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.BranchAssignments)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(u => u.Email.ToLower().Contains(s) || u.FullName.ToLower().Contains(s));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(u => u.CreatedAtUtc)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .Select(u => new UserDto(
                u.Id,
                u.Email,
                u.FullName,
                u.ActorType,
                u.UserRoles.Select(ur => ur.Role.Name).ToList(),
                new List<string>(),
                u.BranchAssignments.FirstOrDefault(b => b.IsPrimary) != null ? u.BranchAssignments.FirstOrDefault(b => b.IsPrimary)!.BranchId : (Guid?)null
            ))
            .ToListAsync();

        return Ok(ApiResponse<PagedResult<UserDto>>.Ok(new PagedResult<UserDto>(items, total, pageIndex, pageSize)));
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest req)
    {
        if (!User.Claims.Any(c => c.Type == ClaimConstants.Permission && (c.Value == Permissions.AdminUsers || c.Value == Permissions.AdminBootstrap)))
        {
            return Forbid();
        }

        var existing = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == req.Email.Trim().ToLower());
        if (existing != null)
        {
            return BadRequest(ApiResponse<Guid>.Fail("User with this email already exists."));
        }

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == req.RoleName);
        if (role == null)
        {
            return BadRequest(ApiResponse<Guid>.Fail($"Role '{req.RoleName}' does not exist."));
        }

        var user = new User
        {
            Email = req.Email.Trim().ToLowerInvariant(),
            FullName = req.FullName.Trim(),
            PasswordHash = _hasher.HashPassword(req.Password),
            ActorType = req.ActorType ?? ClaimConstants.ActorTypeStaff,
            IsActive = true
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });

        if (req.BranchIds != null && req.BranchIds.Any())
        {
            bool first = true;
            foreach (var bId in req.BranchIds)
            {
                _db.UserBranchAssignments.Add(new UserBranchAssignment
                {
                    UserId = user.Id,
                    BranchId = bId,
                    IsPrimary = first
                });
                first = false;
            }
        }

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<Guid>.Ok(user.Id, "User created successfully."));
    }

    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles()
    {
        var roles = await _db.Roles.Select(r => new { r.Id, r.Name, r.Description }).ToListAsync();
        return Ok(ApiResponse<object>.Ok(roles));
    }
}
