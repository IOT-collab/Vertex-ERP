using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;

namespace VertexERP.Services;

public static class AdminReportBuilder
{
    public static async Task<AdminReportViewModel> BuildAsync(ApplicationDbContext db, string? reportType, int? departmentId, int? employeeId, int? managerId, DateOnly? date, string? month, DateOnly? fromDate, DateOnly? toDate, CancellationToken token)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var start = fromDate ?? today;
        var end = toDate ?? start;
        if (DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var monthValue))
        {
            start = new DateOnly(monthValue.Year, monthValue.Month, 1);
            end = start.AddMonths(1).AddDays(-1);
        }
        if (date.HasValue) start = end = date.Value;
        if (end < start) (start, end) = (end, start);

        var departments = await db.Departments.AsNoTracking().Where(item => item.IsActive).OrderBy(item => item.DepartmentName).ToListAsync(token);
        var employees = await db.Employees.AsNoTracking().Include(item => item.ReportingManager).Where(item => item.IsActive).OrderBy(item => item.FullName).ToListAsync(token);
        var managerIds = await db.AppUsers.AsNoTracking().Where(user => user.IsActive && user.Role == AccountRoleService.Manager && user.EmployeeId.HasValue).Select(user => user.EmployeeId!.Value).ToListAsync(token);
        var managers = employees.Where(employee => managerIds.Contains(employee.Id) || employees.Any(item => item.ReportingManagerId == employee.Id)).ToList();
        var title = AdminReportTypes.Label(reportType);
        var columns = new List<string>();
        var rows = new List<IReadOnlyList<string>>();
        var present = 0; var absent = 0; var late = 0;

        IEnumerable<Employee> selectedEmployees = employees;
        if (departmentId.HasValue) selectedEmployees = selectedEmployees.Where(item => item.DepartmentId == departmentId);
        if (employeeId.HasValue) selectedEmployees = selectedEmployees.Where(item => item.Id == employeeId);
        if (managerId.HasValue) selectedEmployees = selectedEmployees.Where(item => item.ReportingManagerId == managerId || item.Id == managerId);
        var selectedList = selectedEmployees.ToList();

        switch (reportType)
        {
            case "department-details":
                columns.AddRange(["Code", "Department", "Manager", "Employees", "Status"]);
                foreach (var department in departments.Where(item => !departmentId.HasValue || item.Id == departmentId))
                    rows.Add([department.DepartmentCode, department.DepartmentName, employees.FirstOrDefault(item => item.Id == department.ManagerId)?.FullName ?? "Not assigned", employees.Count(item => item.DepartmentId == department.Id).ToString(), department.IsActive ? "Active" : "Inactive"]);
                break;
            case "department-manager":
                columns.AddRange(["Department", "Manager", "Manager ID", "Team Size", "Email"]);
                foreach (var department in departments.Where(item => !departmentId.HasValue || item.Id == departmentId))
                { var manager = employees.FirstOrDefault(item => item.Id == department.ManagerId) ?? employees.FirstOrDefault(item => item.DepartmentId == department.Id && employees.Any(e => e.ReportingManagerId == item.Id)); rows.Add([department.DepartmentName, manager?.FullName ?? "Not assigned", manager?.EmployeeCode ?? "--", employees.Count(item => item.ReportingManagerId == manager?.Id).ToString(), manager?.Email ?? "--"]); }
                break;
            case "employee-list":
                columns.AddRange(["Employee ID", "Name", "Department", "Designation", "Manager", "Status"]);
                foreach (var employee in selectedList) rows.Add([employee.EmployeeCode, employee.FullName, employee.Department, employee.Designation, employee.ReportingManager?.FullName ?? "Not assigned", employee.EmployeeStatus]);
                break;
            case "employee-designation":
                columns.AddRange(["Employee ID", "Employee", "Designation", "Department", "Employment Type"]);
                foreach (var employee in selectedList) rows.Add([employee.EmployeeCode, employee.FullName, employee.Designation, employee.Department, employee.EmploymentType]);
                break;
            case "employee-tasks":
            case "manager-projects":
                var tasks = await db.WorkTasks.AsNoTracking().Include(item => item.Assignee).Include(item => item.Manager)
                    .Where(item => (!employeeId.HasValue || item.AssigneeId == employeeId) && (!managerId.HasValue || item.ManagerId == managerId) && (!departmentId.HasValue || item.Assignee.DepartmentId == departmentId)).OrderBy(item => item.Manager.FullName).ThenBy(item => item.DueDate).ToListAsync(token);
                columns.AddRange(reportType == "manager-projects" ? ["Manager", "Project / Work Item", "Assigned To", "Status", "Due Date", "Priority"] : ["Employee", "Task", "Manager", "Status", "Due Date", "Priority"]);
                foreach (var task in tasks) rows.Add(reportType == "manager-projects" ? [task.Manager.FullName, task.Title, task.Assignee.FullName, task.Status, task.DueDate.ToString("dd MMM yyyy"), task.Priority] : [task.Assignee.FullName, task.Title, task.Manager.FullName, task.Status, task.DueDate.ToString("dd MMM yyyy"), task.Priority]);
                break;
            case "employee-attendance":
            case "department-attendance":
            case "daily-attendance":
            case "monthly-attendance":
            case "punch-report":
            case "attendance-summary":
                var from = start.ToDateTime(TimeOnly.MinValue); var until = end.AddDays(1).ToDateTime(TimeOnly.MinValue);
                var ids = selectedList.Select(item => item.Id).ToList();
                var logs = await db.AttendanceLogs.AsNoTracking().Where(item => item.EmployeeId.HasValue && ids.Contains(item.EmployeeId.Value) && item.PunchTime >= from && item.PunchTime < until).OrderBy(item => item.PunchTime).ToListAsync(token);
                if (reportType == "attendance-summary")
                {
                    columns.AddRange(["Employee ID", "Employee", "Department", "Present Days", "Late Days", "Absent Days"]);
                    foreach (var employee in selectedList)
                    {
                        var counts = AttendanceRows(employee, logs, start, end); present += counts.Count(x => x.Status == "Present"); late += counts.Count(x => x.Status == "Late"); absent += counts.Count(x => x.Status == "Absent");
                        rows.Add([employee.EmployeeCode, employee.FullName, employee.Department, counts.Count(x => x.Status == "Present").ToString(), counts.Count(x => x.Status == "Late").ToString(), counts.Count(x => x.Status == "Absent").ToString()]);
                    }
                }
                else
                {
                    columns.AddRange(["Date", "Employee ID", "Employee", "Department", "Check In", "Check Out", "Punches", "Status"]);
                    foreach (var employee in selectedList)
                        foreach (var item in AttendanceRows(employee, logs, start, end)) { if (item.Status == "Present") present++; else if (item.Status == "Late") late++; else absent++; rows.Add([item.Date.ToString("dd MMM yyyy"), employee.EmployeeCode, employee.FullName, employee.Department, item.CheckIn, item.CheckOut, item.Punches.ToString(), item.Status]); }
                }
                break;
        }

        return new AdminReportViewModel { ReportType = reportType, ReportTitle = title, DepartmentId = departmentId, EmployeeId = employeeId, ManagerId = managerId, Date = date, Month = month, FromDate = start, ToDate = end, Departments = departments, Employees = employees, Managers = managers, Columns = columns, Rows = rows, PresentCount = present, AbsentCount = absent, LateCount = late };
    }

    private static List<(DateOnly Date, string CheckIn, string CheckOut, int Punches, string Status)> AttendanceRows(Employee employee, IReadOnlyList<AttendanceLog> logs, DateOnly start, DateOnly end)
    {
        var result = new List<(DateOnly, string, string, int, string)>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            var punches = logs.Where(item => item.EmployeeId == employee.Id && DateOnly.FromDateTime(item.PunchTime) == day).OrderBy(item => item.PunchTime).ToList();
            var first = punches.FirstOrDefault()?.PunchTime; var last = punches.Count > 1 ? punches[^1].PunchTime : (DateTime?)null;
            var status = !first.HasValue ? "Absent" : TimeOnly.FromDateTime(first.Value) > new TimeOnly(9, 30) ? "Late" : "Present";
            result.Add((day, first?.ToString("hh:mm tt") ?? "--", last?.ToString("hh:mm tt") ?? "--", punches.Count, status));
        }
        return result;
    }
}
