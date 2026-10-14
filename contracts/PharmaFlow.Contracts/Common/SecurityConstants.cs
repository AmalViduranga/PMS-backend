namespace PharmaFlow.Contracts.Common;

public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Pharmacist = "Pharmacist";
    public const string InventoryManager = "InventoryManager";
    public const string Cashier = "Cashier";
    public const string HrManager = "HrManager";
    public const string Customer = "Customer";
}

public static class Permissions
{
    public const string AdminBootstrap = "admin:bootstrap";
    public const string AdminUsers = "admin:users";
    public const string AdminBranches = "admin:branches";

    public const string CatalogueRead = "catalogue:read";
    public const string CatalogueWrite = "catalogue:write";

    public const string PurchasingRead = "purchasing:read";
    public const string PurchasingWrite = "purchasing:write";
    public const string PurchasingApprove = "purchasing:approve";

    public const string InventoryRead = "inventory:read";
    public const string InventoryAdjust = "inventory:adjust";
    public const string InventoryTransfer = "inventory:transfer";

    public const string PosTransact = "pos:transact";
    public const string PosDiscount = "pos:discount";
    public const string PosRefund = "pos:refund";
    public const string ShiftManage = "pos:shift";

    public const string PrescriptionReview = "prescriptions:review";
    public const string PrescriptionDispense = "prescriptions:dispense";

    public const string WorkforceManage = "workforce:manage";
    public const string WorkforceLeaveApprove = "workforce:leave:approve";

    public const string ReportsView = "reports:view";
}

public static class ClaimConstants
{
    public const string UserId = "sub";
    public const string Email = "email";
    public const string Role = "role";
    public const string Permission = "permission";
    public const string BranchId = "branch_id";
    public const string ActorType = "actor_type";
    public const string FullName = "name";

    public const string ActorTypeStaff = "staff";
    public const string ActorTypeCustomer = "customer";
}

public static class ClaimsPrincipalExtensions
{
    public static string? GetUserId(this System.Security.Claims.ClaimsPrincipal principal)
    {
        return principal.FindFirst(ClaimConstants.UserId)?.Value 
            ?? principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    }
}
