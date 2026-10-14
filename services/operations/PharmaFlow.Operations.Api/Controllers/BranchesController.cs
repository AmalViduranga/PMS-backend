using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Contracts.Common;
using PharmaFlow.Operations.Api.Data;
using PharmaFlow.Operations.Api.Domain;
using PharmaFlow.Operations.Api.DTOs;

namespace PharmaFlow.Operations.Api.Controllers;

[ApiController]
[Route("api/branches")]
[Authorize]
public class BranchesController : ControllerBase
{
    private readonly OperationsDbContext _db;

    public BranchesController(OperationsDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetBranches()
    {
        var branches = await _db.Branches
            .OrderBy(b => b.Name)
            .Select(b => new BranchDto(b.Id, b.Code, b.Name, b.Address, b.Phone, b.Email, b.Timezone, b.Currency, b.IsActive))
            .ToListAsync();

        return Ok(ApiResponse<List<BranchDto>>.Ok(branches));
    }

    [HttpPost]
    public async Task<IActionResult> CreateBranch([FromBody] CreateBranchRequest req)
    {
        if (!User.Claims.Any(c => c.Type == ClaimConstants.Permission && (c.Value == Permissions.AdminBranches || c.Value == Permissions.AdminBootstrap)))
        {
            return Forbid();
        }

        var org = await _db.Organizations.FirstOrDefaultAsync();
        if (org == null)
        {
            org = new Organization();
            _db.Organizations.Add(org);
            await _db.SaveChangesAsync();
        }

        var existing = await _db.Branches.FirstOrDefaultAsync(b => b.Code.ToLower() == req.Code.Trim().ToLower());
        if (existing != null)
        {
            return BadRequest(ApiResponse<Guid>.Fail("Branch with this code already exists."));
        }

        var branch = new Branch
        {
            OrganizationId = org.Id,
            Code = req.Code.Trim().ToUpperInvariant(),
            Name = req.Name.Trim(),
            Address = req.Address.Trim(),
            Phone = req.Phone.Trim(),
            Email = req.Email.Trim(),
            Timezone = req.Timezone ?? "Asia/Colombo",
            Currency = req.Currency ?? "LKR",
            IsActive = true
        };

        _db.Branches.Add(branch);
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<Guid>.Ok(branch.Id, "Branch created successfully."));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetBranchById(Guid id)
    {
        var b = await _db.Branches.FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return NotFound(ApiResponse<BranchDto>.Fail("Branch not found."));
        return Ok(ApiResponse<BranchDto>.Ok(new BranchDto(b.Id, b.Code, b.Name, b.Address, b.Phone, b.Email, b.Timezone, b.Currency, b.IsActive)));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateBranch(Guid id, [FromBody] UpdateBranchRequest req)
    {
        if (!User.Claims.Any(c => c.Type == ClaimConstants.Permission && (c.Value == Permissions.AdminBranches || c.Value == Permissions.AdminBootstrap)))
        {
            return Forbid();
        }

        var b = await _db.Branches.FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return NotFound(ApiResponse<bool>.Fail("Branch not found."));

        b.Name = req.Name.Trim();
        b.Address = req.Address.Trim();
        b.Phone = req.Phone.Trim();
        b.Email = req.Email.Trim();
        if (!string.IsNullOrWhiteSpace(req.Timezone)) b.Timezone = req.Timezone;
        if (!string.IsNullOrWhiteSpace(req.Currency)) b.Currency = req.Currency;
        if (req.IsActive.HasValue) b.IsActive = req.IsActive.Value;

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true, "Branch updated successfully."));
    }

    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateBranchStatus(Guid id, [FromBody] UpdateBranchStatusRequest req)
    {
        if (!User.Claims.Any(c => c.Type == ClaimConstants.Permission && (c.Value == Permissions.AdminBranches || c.Value == Permissions.AdminBootstrap)))
        {
            return Forbid();
        }

        var b = await _db.Branches.FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return NotFound(ApiResponse<bool>.Fail("Branch not found."));

        b.IsActive = req.IsActive;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true, $"Branch successfully {(req.IsActive ? "activated" : "deactivated")}."));
    }

    [HttpGet("registers")]
    public async Task<IActionResult> GetRegisters([FromQuery] Guid? branchId)
    {
        var query = _db.Registers.AsQueryable();
        if (branchId.HasValue) query = query.Where(r => r.BranchId == branchId.Value);

        var registers = await query
            .Select(r => new RegisterDto(r.Id, r.BranchId, r.Code, r.Name, r.HardwareIdentifier, r.IsActive))
            .ToListAsync();

        return Ok(ApiResponse<List<RegisterDto>>.Ok(registers));
    }

    [HttpPost("registers")]
    public async Task<IActionResult> CreateRegister([FromBody] CreateRegisterRequest req)
    {
        if (!User.Claims.Any(c => c.Type == ClaimConstants.Permission && (c.Value == Permissions.AdminBranches || c.Value == Permissions.AdminBootstrap)))
        {
            return Forbid();
        }

        var branch = await _db.Branches.FirstOrDefaultAsync(b => b.Id == req.BranchId);
        if (branch == null) return BadRequest(ApiResponse<Guid>.Fail("Branch not found."));

        var register = new Register
        {
            BranchId = req.BranchId,
            Code = req.Code.Trim().ToUpperInvariant(),
            Name = req.Name.Trim(),
            HardwareIdentifier = req.HardwareIdentifier,
            IsActive = true
        };

        _db.Registers.Add(register);
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<Guid>.Ok(register.Id, "Register created successfully."));
    }
}
