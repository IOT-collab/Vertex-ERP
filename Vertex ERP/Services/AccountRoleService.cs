namespace VertexERP.Services;

public static class AccountRoleService
{
    public const string Admin = "Admin";
    public const string HR = "HR";
    public const string Manager = "Manager";
    public const string Employee = "Employee";

    public static string? Normalize(string? role)
    {
        if (string.Equals(role, Admin, StringComparison.OrdinalIgnoreCase)) return Admin;
        if (string.Equals(role, HR, StringComparison.OrdinalIgnoreCase)) return HR;
        if (string.Equals(role, Manager, StringComparison.OrdinalIgnoreCase) || string.Equals(role, "Supervisor", StringComparison.OrdinalIgnoreCase)) return Manager;
        if (string.Equals(role, Employee, StringComparison.OrdinalIgnoreCase) || string.Equals(role, "User", StringComparison.OrdinalIgnoreCase)) return Employee;
        return null;
    }
}
