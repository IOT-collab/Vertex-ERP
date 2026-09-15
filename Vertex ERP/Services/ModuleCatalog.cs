namespace VertexERP.Services;
public sealed record ModuleDefinition(string Id, string Name, string Description, string Controller, string Action);
public static class ModuleCatalog
{
    public static readonly ModuleDefinition[] All = [
        new("settings", "System Configurations", "Application settings.", "Main", "Settings"),
        new("hr", "Human Resources", "Employees, departments, recruitment and employee profiles.", "Main", "Hrms"),
        new("attendance", "Attendance & Shifts", "Attendance, field location and biometric access. Device collection continues to preserve punches.", "Main", "Attendence"),
        new("inventory", "Assets & Inventory", "Employee assets and asset allocation.", "Hr", "AssetManagement"),
        new("payroll", "Payroll & Compensation", "Salary slips and bank detail requests.", "Hr", "SalarySlips"),
        new("leave", "Leave & Queries", "Leave requests, approvals and employee queries.", "Main", "WorkflowManagement"),
        new("tasks", "Task Management", "Tasks, assignments and progress updates.", "Main", "TaskMgm"),
        new("projects", "Project Management", "Projects and manager project lists.", "Main", "ProjectMgm"),
        new("documents", "Document Management", "Employee document generation and downloads.", "Hr", "EmpDocuments"),
        new("expenses", "Expenses", "Expense claims and review.", "Expense", "Index"),
        new("shipments", "Shipment Tracking", "Shipment tracking and status lookup.", "Main", "OrderTracking"),
        new("reports", "Reports", "Administrative reports and exports.", "Main", "Reports")
    ];
    public static string? ForRoute(string? controller, string? action)
    {
        var c = controller?.ToLowerInvariant(); var a = action?.ToLowerInvariant() ?? "";
        if (c == "moduleadmin" || c == "audit") return null;
        // Device ingress remains available to retain punches while user access is disabled.
        if (c is "biometricapi" or "zkadms") return null;
        if (c == "biometricdevices") return "attendance";
        if (c == "expense") return "expenses";
        if (c == "taskmanagement") return "tasks";
        if (c == "projectmgm") return "projects";
        if (c is "employee" or "employeedetails") return "hr";
        if (c is not ("main" or "hr")) return null;
        if (a.Contains("salary") || a.Contains("bank")) return "payroll";
        if (a.Contains("attendance") || a == "attendence" || a.Contains("locationtracking")) return "attendance";
        if (a.Contains("asset")) return "inventory";
        if (a.Contains("document") || a is "documentmgm" or "adddocmgmsave") return "documents";
        if (a.Contains("leave") || a.Contains("quer") || a is "workflowmanagement" or "closedissues") return "leave";
        if (a.Contains("task")) return "tasks";
        if (a.Contains("project")) return "projects";
        if (a is "reports" or "downloadadminreport") return "reports";
        if (a == "ordertracking") return "shipments";
        if (a == "settings") return "settings";
        if (c == "hr" || a is "employees" or "hrms" or "manager" or "addemphrm" or "empaddrequirement" or "departmentmanagement" or "employeeprofile" or "editemployeeprofile") return "hr";
        return null;
    }
}

