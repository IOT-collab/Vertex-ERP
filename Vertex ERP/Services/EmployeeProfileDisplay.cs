using VertexERP.Models;

namespace VertexERP.Services;

public static class EmployeeProfileDisplay
{
    // HR-completed profiles are authoritative; pending imports still use WDMS.
    public static string Name(Employee? employee, string? wdmsName)
    {
        if (employee != null && !employee.IsBiometricProfilePending
            && !string.IsNullOrWhiteSpace(employee.FullName)
            && !employee.FullName.StartsWith("Biometric User", StringComparison.OrdinalIgnoreCase))
            return employee.FullName;
        if (!string.IsNullOrWhiteSpace(wdmsName)) return wdmsName.Trim();
        if (employee == null) return "Name unavailable in WDMS";
        return employee != null && !employee.FullName.StartsWith("Biometric User", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(employee.FullName) ? employee.FullName : "Name not available in WDMS";
    }
}
