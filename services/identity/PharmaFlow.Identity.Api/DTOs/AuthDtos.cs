namespace PharmaFlow.Identity.Api.DTOs;

public record BootstrapAdminRequest(
    string Email,
    string Password,
    string FullName,
    string BootstrapToken);

public record LoginRequest(
    string Email,
    string Password);

public record LoginResponse(
    string AccessToken,
    DateTime ExpiresAtUtc,
    string RefreshToken,
    UserDto User);

public record RefreshTokenRequest(
    string RefreshToken);

public record UserDto(
    Guid Id,
    string Email,
    string FullName,
    string ActorType,
    List<string> Roles,
    List<string> Permissions,
    Guid? PrimaryBranchId);

public record ForgotPasswordRequest(
    string Email);

public record ResetPasswordRequest(
    string Email,
    string ResetToken,
    string NewPassword);

public record CreateUserRequest(
    string Email,
    string FullName,
    string Password,
    string RoleName,
    List<Guid>? BranchIds,
    string ActorType);

public record AssignBranchRequest(
    Guid BranchId,
    bool IsPrimary);
