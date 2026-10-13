using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PharmaFlow.Contracts.Common;
using PharmaFlow.Identity.Api.Data;
using PharmaFlow.Identity.Api.Domain;

namespace PharmaFlow.Identity.Api.Services;

public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string passwordHash);
}

public class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, HashSize);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool VerifyPassword(string password, string passwordHash)
    {
        var parts = passwordHash.Split('.');
        if (parts.Length != 2) return false;

        byte[] salt = Convert.FromBase64String(parts[0]);
        byte[] hash = Convert.FromBase64String(parts[1]);

        byte[] inputHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, HashSize);
        return CryptographicOperations.FixedTimeEquals(hash, inputHash);
    }
}

public interface ITokenService
{
    (string Token, DateTime ExpiresAtUtc) GenerateAccessToken(User user, IEnumerable<string> roles, IEnumerable<string> permissions, Guid? primaryBranchId);
    RefreshToken GenerateRefreshToken(Guid userId);
}

public class TokenService : ITokenService
{
    private readonly IConfiguration _configuration;

    public TokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public (string Token, DateTime ExpiresAtUtc) GenerateAccessToken(User user, IEnumerable<string> roles, IEnumerable<string> permissions, Guid? primaryBranchId)
    {
        var secret = _configuration["Jwt:Secret"] ?? "PharmaFlow_Ultra_Secure_Dev_Signing_Key_2026_AtLeast32Bytes!";
        var issuer = _configuration["Jwt:Issuer"] ?? "PharmaFlow.Identity";
        var audience = _configuration["Jwt:Audience"] ?? "PharmaFlow.Services";
        var expiryMinutes = int.TryParse(_configuration["Jwt:ExpiryMinutes"], out var exp) ? exp : 60;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimConstants.UserId, user.Id.ToString()),
            new(ClaimConstants.Email, user.Email),
            new(ClaimConstants.FullName, user.FullName),
            new(ClaimConstants.ActorType, user.ActorType),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (primaryBranchId.HasValue)
        {
            claims.Add(new Claim(ClaimConstants.BranchId, primaryBranchId.Value.ToString()));
        }

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimConstants.Role, role));
        }

        foreach (var permission in permissions)
        {
            claims.Add(new Claim(ClaimConstants.Permission, permission));
        }

        var expiresAtUtc = DateTime.UtcNow.AddMinutes(expiryMinutes);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAtUtc,
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = creds
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(tokenDescriptor);
        return (handler.WriteToken(token), expiresAtUtc);
    }

    public RefreshToken GenerateRefreshToken(Guid userId)
    {
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        var tokenString = Convert.ToBase64String(randomBytes);
        var days = int.TryParse(_configuration["Jwt:RefreshTokenExpiryDays"], out var d) ? d : 7;

        return new RefreshToken
        {
            UserId = userId,
            Token = tokenString,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(days)
        };
    }
}

public interface IBootstrapService
{
    Task<bool> IsBootstrappedAsync();
    Task<string> GetOrGenerateBootstrapTokenAsync();
    Task<ApiResponse<Guid>> BootstrapAdminAsync(string email, string password, string fullName, string providedToken);
    Task SeedDefaultRolesAndPermissionsAsync();
}

public class BootstrapService : IBootstrapService
{
    private readonly IdentityDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IConfiguration _config;
    private readonly ILogger<BootstrapService> _logger;

    public BootstrapService(IdentityDbContext db, IPasswordHasher hasher, IConfiguration config, ILogger<BootstrapService> logger)
    {
        _db = db;
        _hasher = hasher;
        _config = config;
        _logger = logger;
    }

    public async Task<bool> IsBootstrappedAsync()
    {
        var state = await _db.SystemBootstrapStates.FirstOrDefaultAsync(s => s.Id == 1);
        if (state != null && state.IsBootstrapped) return true;

        var superAdminRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == Roles.SuperAdmin);
        if (superAdminRole != null)
        {
            var hasSuperAdmin = await _db.UserRoles.AnyAsync(ur => ur.RoleId == superAdminRole.Id);
            if (hasSuperAdmin) return true;
        }

        return false;
    }

    public async Task<string> GetOrGenerateBootstrapTokenAsync()
    {
        var configured = _config["Bootstrap:MasterBootstrapKey"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        var state = await _db.SystemBootstrapStates.FirstOrDefaultAsync(s => s.Id == 1);
        if (state == null)
        {
            var generatedToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            state = new SystemBootstrapState
            {
                Id = 1,
                IsBootstrapped = false,
                BootstrapTokenHash = _hasher.HashPassword(generatedToken)
            };
            _db.SystemBootstrapStates.Add(state);
            await _db.SaveChangesAsync();
            _logger.LogWarning("System not bootstrapped. Generated initial bootstrap token: {Token}", generatedToken);
            return generatedToken;
        }

        return string.Empty;
    }

    public async Task SeedDefaultRolesAndPermissionsAsync()
    {
        var standardRoles = new Dictionary<string, (string Desc, List<string> Perms)>
        {
            [Roles.SuperAdmin] = ("Full system administrative access", new()
            {
                Permissions.AdminBootstrap, Permissions.AdminUsers, Permissions.AdminBranches,
                Permissions.CatalogueRead, Permissions.CatalogueWrite,
                Permissions.PurchasingRead, Permissions.PurchasingWrite, Permissions.PurchasingApprove,
                Permissions.InventoryRead, Permissions.InventoryAdjust, Permissions.InventoryTransfer,
                Permissions.PosTransact, Permissions.PosDiscount, Permissions.PosRefund, Permissions.ShiftManage,
                Permissions.PrescriptionReview, Permissions.PrescriptionDispense,
                Permissions.WorkforceManage, Permissions.WorkforceLeaveApprove,
                Permissions.ReportsView
            }),
            [Roles.Pharmacist] = ("Licensed pharmacist role", new()
            {
                Permissions.CatalogueRead, Permissions.InventoryRead,
                Permissions.PosTransact, Permissions.ShiftManage,
                Permissions.PrescriptionReview, Permissions.PrescriptionDispense,
                Permissions.ReportsView
            }),
            [Roles.InventoryManager] = ("Inventory and purchasing supervisor", new()
            {
                Permissions.CatalogueRead, Permissions.CatalogueWrite,
                Permissions.PurchasingRead, Permissions.PurchasingWrite, Permissions.PurchasingApprove,
                Permissions.InventoryRead, Permissions.InventoryAdjust, Permissions.InventoryTransfer,
                Permissions.ReportsView
            }),
            [Roles.Cashier] = ("Point of sale cashier", new()
            {
                Permissions.CatalogueRead, Permissions.InventoryRead,
                Permissions.PosTransact, Permissions.PosDiscount, Permissions.ShiftManage
            }),
            [Roles.HrManager] = ("Human resources manager", new()
            {
                Permissions.WorkforceManage, Permissions.WorkforceLeaveApprove, Permissions.ReportsView
            }),
            [Roles.Customer] = ("Registered online storefront customer", new())
        };

        foreach (var (roleName, (desc, perms)) in standardRoles)
        {
            var existingRole = await _db.Roles.Include(r => r.RolePermissions).FirstOrDefaultAsync(r => r.Name == roleName);
            if (existingRole == null)
            {
                existingRole = new Role
                {
                    Name = roleName,
                    Description = desc
                };
                _db.Roles.Add(existingRole);
                await _db.SaveChangesAsync();
            }

            var existingPermNames = existingRole.RolePermissions.Select(p => p.Permission).ToHashSet();
            foreach (var perm in perms)
            {
                if (!existingPermNames.Contains(perm))
                {
                    _db.RolePermissions.Add(new RolePermission
                    {
                        RoleId = existingRole.Id,
                        Permission = perm
                    });
                }
            }
        }

        await _db.SaveChangesAsync();
    }

    public async Task<ApiResponse<Guid>> BootstrapAdminAsync(string email, string password, string fullName, string providedToken)
    {
        if (await IsBootstrappedAsync())
        {
            return ApiResponse<Guid>.Fail("System has already been bootstrapped. First-admin creation is permanently disabled.");
        }

        var configuredToken = _config["Bootstrap:MasterBootstrapKey"];
        bool tokenValid = false;

        if (!string.IsNullOrWhiteSpace(configuredToken))
        {
            tokenValid = string.Equals(configuredToken, providedToken, StringComparison.Ordinal);
        }
        else
        {
            var state = await _db.SystemBootstrapStates.FirstOrDefaultAsync(s => s.Id == 1);
            if (state != null && !string.IsNullOrWhiteSpace(state.BootstrapTokenHash))
            {
                tokenValid = _hasher.VerifyPassword(providedToken, state.BootstrapTokenHash);
            }
        }

        if (!tokenValid)
        {
            return ApiResponse<Guid>.Fail("Invalid bootstrap token.");
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 12 ||
            !password.Any(char.IsUpper) || !password.Any(char.IsLower) ||
            !password.Any(char.IsDigit) || !password.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            return ApiResponse<Guid>.Fail("Password must be at least 12 characters and contain uppercase, lowercase, digit, and special character.");
        }

        await SeedDefaultRolesAndPermissionsAsync();

        var superAdminRole = await _db.Roles.FirstAsync(r => r.Name == Roles.SuperAdmin);

        var existingUser = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
        if (existingUser != null)
        {
            return ApiResponse<Guid>.Fail("A user with this email address already exists.");
        }

        var adminUser = new User
        {
            Email = email.Trim().ToLowerInvariant(),
            FullName = fullName.Trim(),
            PasswordHash = _hasher.HashPassword(password),
            ActorType = ClaimConstants.ActorTypeStaff,
            IsActive = true
        };

        _db.Users.Add(adminUser);
        await _db.SaveChangesAsync();

        _db.UserRoles.Add(new UserRole
        {
            UserId = adminUser.Id,
            RoleId = superAdminRole.Id
        });

        var bootstrapState = await _db.SystemBootstrapStates.FirstOrDefaultAsync(s => s.Id == 1);
        if (bootstrapState == null)
        {
            bootstrapState = new SystemBootstrapState { Id = 1 };
            _db.SystemBootstrapStates.Add(bootstrapState);
        }

        bootstrapState.IsBootstrapped = true;
        bootstrapState.BootstrappedAtUtc = DateTime.UtcNow;
        bootstrapState.BootstrappedByEmail = adminUser.Email;
        bootstrapState.BootstrapTokenHash = null; // permanently invalidate

        await _db.SaveChangesAsync();

        _logger.LogInformation("System successfully bootstrapped with SuperAdmin: {Email}", adminUser.Email);
        return ApiResponse<Guid>.Ok(adminUser.Id, "SuperAdmin successfully created. Bootstrap is now closed.");
    }
}
