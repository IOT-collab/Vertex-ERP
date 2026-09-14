using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;

namespace VertexERP.Services;

public static class AdminReportBuilder
{
    public static async Task<AdminReportViewModel> BuildAsync(ApplicationDbContext db, string? reportType, int? departmentId, int? employeeId, int? managerId, DateOnly? date, string? month, DateOnly? fromDate, DateOnly? toDate, CancellationToken token, int? projectId = null, string? status = null)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        // Only the selected report's date controls may affect its results or exports.
        date = reportType == "daily-attendance" ? date ?? today : null;
        month = reportType == "monthly-attendance" ? month ?? today.ToString("yyyy-MM", CultureInfo.InvariantCulture) : null;
        if (!string.IsNullOrEmpty(month) && (!DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedMonth) || parsedMonth.Year == 9999)) throw new ArgumentException("Select a valid month.");
        var start = fromDate ?? today;
        var end = toDate ?? start;
        if (DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var monthValue))
        {
            start = new DateOnly(monthValue.Year, monthValue.Month, 1);
            end = start.AddMonths(1).AddDays(-1);
        }
        if (date.HasValue) start = end = date.Value;
        if (end < start) throw new ArgumentException("To date must be on or after From date.");
        if (end.DayNumber - start.DayNumber > 365 || end == DateOnly.MaxValue) throw new ArgumentException("Choose a date range of 366 days or less, ending before 31 December 9999.");

        var departments = await db.Departments.AsNoTracking().OrderBy(item => item.DepartmentName).ToListAsync(token);
        var employees = await db.Employees.AsNoTracking().Include(item => item.ReportingManager).OrderBy(item => item.FullName).ToListAsync(token);
        var projects = await db.Projects.AsNoTracking().Include(item => item.Manager).Include(item => item.Department).OrderBy(item => item.ProjectName).ToListAsync(token);
        var managerIds = await db.AppUsers.AsNoTracking().Where(user => user.IsActive && user.Role == AccountRoleService.Manager && user.EmployeeId.HasValue).Select(user => user.EmployeeId!.Value).ToListAsync(token);
        var managers = employees.Where(employee => managerIds.Contains(employee.Id) || employees.Any(item => item.ReportingManagerId == employee.Id)).ToList();
        var title = AdminReportTypes.Label(reportType);
        var columns = new List<string>();
        var rows = new List<IReadOnlyList<string>>();
        var present = 0; var absent = 0; var late = 0;
        if (departmentId.HasValue && !departments.Any(x => x.Id == departmentId)) throw new ArgumentException("Department not found.");
        if (employeeId.HasValue && !employees.Any(x => x.Id == employeeId)) throw new ArgumentException("Employee not found.");
        if (managerId.HasValue && !managers.Any(x => x.Id == managerId)) throw new ArgumentException("Manager not found.");
        if (projectId.HasValue && !projects.Any(x => x.Id == projectId)) throw new ArgumentException("Project not found.");

        IEnumerable<Employee> selectedEmployees = employees;
        if (departmentId.HasValue) selectedEmployees = selectedEmployees.Where(item => item.DepartmentId == departmentId);
        if (employeeId.HasValue) selectedEmployees = selectedEmployees.Where(item => item.Id == employeeId);
        if (managerId.HasValue) selectedEmployees = selectedEmployees.Where(item => item.ReportingManagerId == managerId || item.Id == managerId);
        var selectedList = selectedEmployees.ToList();
        if ((reportType is "employee-list" or "employee-designation") && !string.IsNullOrEmpty(status)) selectedList = selectedList.Where(x => (x.IsActive ? "Active" : "Inactive") == status).ToList();

        switch (reportType)
        {
            case "project-details":
                columns.AddRange(["Code", "Project", "Department", "Manager", "Start", "End", "Status", "Description"]);
                foreach (var p in projects.Where(x => (!projectId.HasValue || x.Id == projectId) && (!departmentId.HasValue || x.DepartmentId == departmentId) && (!managerId.HasValue || x.ManagerId == managerId) && (string.IsNullOrEmpty(status) || x.Status == status) && x.StartDate <= end && x.EndDate >= start))
                    rows.Add([p.ProjectCode, p.ProjectName, p.Department?.DepartmentName ?? "Not assigned", p.Manager?.FullName ?? "Not assigned", p.StartDate.ToString("dd MMM yyyy"), p.EndDate.ToString("dd MMM yyyy"), p.Status, p.Description ?? ""]);
                break;
            case "leave-report":
                columns.AddRange(["Employee ID", "Employee", "Department", "Leave Type", "From", "To", "Status", "Reason"]);
                var leaveIds = selectedList.Select(x => x.Id).ToList();
                var leaves = await db.LeaveRequests.AsNoTracking().Include(x => x.Employee).Where(x => leaveIds.Contains(x.EmployeeId) && x.FromDate <= end && x.ToDate >= start && (string.IsNullOrEmpty(status) || x.Status == status)).OrderBy(x => x.FromDate).ToListAsync(token);
                foreach (var l in leaves) rows.Add([l.Employee.EmployeeCode, l.Employee.FullName, l.Employee.Department, l.LeaveType, l.FromDate.ToString("dd MMM yyyy"), l.ToDate.ToString("dd MMM yyyy"), l.Status, l.Reason]);
                break;
            case "manager-report":
                columns.AddRange(["Manager ID", "Manager", "Department", "Team", "Projects", "Tasks", "Completed", "Overdue", "Pending Leaves"]);
                var work = await db.WorkTasks.AsNoTracking().Where(x => x.DueDate >= start && x.DueDate <= end).ToListAsync(token);
                var pending = await db.LeaveRequests.AsNoTracking().Where(x => x.Status == "Pending" && x.FromDate <= end && x.ToDate >= start).ToListAsync(token);
                foreach (var m in managers.Where(x => (!managerId.HasValue || x.Id == managerId) && (!departmentId.HasValue || x.DepartmentId == departmentId)))
                {
                    var team = employees.Where(x => x.IsActive && x.ReportingManagerId == m.Id).Select(x => x.Id).ToHashSet();
                    var mt = work.Where(x => x.ManagerId == m.Id).ToList();
                    rows.Add([m.EmployeeCode, m.FullName, m.Department, team.Count.ToString(), projects.Count(x => x.ManagerId == m.Id && x.StartDate <= end && x.EndDate >= start).ToString(), mt.Count.ToString(), mt.Count(x => x.Status == "Completed").ToString(), mt.Count(x => x.Status != "Completed" && x.DueDate < today).ToString(), pending.Count(x => x.AssignedApproverEmployeeId == m.Id || (x.AssignedApproverEmployeeId == null && team.Contains(x.EmployeeId))).ToString()]);
                }
                break;
            case "department-details":
                columns.AddRange(["Code", "Department", "Manager", "Employees", "Status"]);
                foreach (var department in departments.Where(item => !departmentId.HasValue || item.Id == departmentId))
                    rows.Add([department.DepartmentCode, department.DepartmentName, employees.FirstOrDefault(item => item.Id == department.ManagerId)?.FullName ?? "Not assigned", employees.Count(item => item.DepartmentId == department.Id).ToString(), department.IsActive ? "Active" : "Inactive"]);
                break;
            case "department-manager":
                columns.AddRange(["Department", "Manager", "Manager ID", "Team Size", "Email"]);
                foreach (var department in departments.Where(item => !departmentId.HasValue || item.Id == departmentId))
                { var manager = employees.FirstOrDefault(item => item.Id == department.ManagerId) ?? employees.FirstOrDefault(item => item.DepartmentId == department.Id && employees.Any(e => e.ReportingManagerId == item.Id)); rows.Add([department.DepartmentName, manager?.FullName ?? "Not assigned", manager?.EmployeeCode ?? "--", (manager == null ? 0 : employees.Count(item => item.ReportingManagerId == manager.Id)).ToString(), manager?.Email ?? "--"]); }
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
                    .Where(item => (!employeeId.HasValue || item.AssigneeId == employeeId) && (!managerId.HasValue || item.ManagerId == managerId) && (!departmentId.HasValue || item.Assignee.DepartmentId == departmentId) && item.DueDate >= start && item.DueDate <= end && (string.IsNullOrEmpty(status) || item.Status == status)).OrderBy(item => item.Manager.FullName).ThenBy(item => item.DueDate).ToListAsync(token);
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
                var approvedLeaves = await db.LeaveRequests.AsNoTracking().Where(x => ids.Contains(x.EmployeeId) && x.Status == "Approved" && x.FromDate <= end && x.ToDate >= start).ToListAsync(token);
                if (reportType == "attendance-summary")
                {
                    columns.AddRange(["Employee ID", "Employee", "Department", "Present Days", "Late Days", "Absent Days"]);
                    foreach (var employee in selectedList)
                    {
                        var counts = AttendanceRows(employee, logs, start, end, approvedLeaves); present += counts.Count(x => x.Status == "Present"); late += counts.Count(x => x.Status == "Late"); absent += counts.Count(x => x.Status == "Absent");
                        rows.Add([employee.EmployeeCode, employee.FullName, employee.Department, counts.Count(x => x.Status == "Present").ToString(), counts.Count(x => x.Status == "Late").ToString(), counts.Count(x => x.Status == "Absent").ToString()]);
                    }
                }
                else
                {
                    columns.AddRange(["Date", "Employee ID", "Employee", "Department", "Check In", "Check Out", "Punches", "Status"]);
                    foreach (var employee in selectedList)
                        foreach (var item in AttendanceRows(employee, logs, start, end, approvedLeaves).Where(x => string.IsNullOrEmpty(status) || x.Status == status)) { if (item.Status == "Present") present++; else if (item.Status == "Late") late++; else if (item.Status == "Absent") absent++; rows.Add([item.Date.ToString("dd MMM yyyy"), employee.EmployeeCode, employee.FullName, employee.Department, item.CheckIn, item.CheckOut, item.Punches.ToString(), item.Status]); }
                }
                break;
        }

        return new AdminReportViewModel { ReportType = reportType, ReportTitle = title, DepartmentId = departmentId, EmployeeId = employeeId, ManagerId = managerId, ProjectId = projectId, Status = status, Projects = projects, FilterSummary = $"Department: {departments.FirstOrDefault(x => x.Id == departmentId)?.DepartmentName ?? "All"} | Employee: {employees.FirstOrDefault(x => x.Id == employeeId)?.FullName ?? "All"} | Manager: {managers.FirstOrDefault(x => x.Id == managerId)?.FullName ?? "All"} | Project: {projects.FirstOrDefault(x => x.Id == projectId)?.ProjectName ?? "All"} | Status: {status ?? "All"}", Date = date, Month = month, FromDate = start, ToDate = end, Departments = departments, Employees = employees, Managers = managers, Columns = columns, Rows = rows, PresentCount = present, AbsentCount = absent, LateCount = late };
    }

    private static List<(DateOnly Date, string CheckIn, string CheckOut, int Punches, string Status)> AttendanceRows(Employee employee, IReadOnlyList<AttendanceLog> logs, DateOnly start, DateOnly end, IReadOnlyList<LeaveRequest> approvedLeaves)
    {
        var result = new List<(DateOnly, string, string, int, string)>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            var punches = logs.Where(item => item.EmployeeId == employee.Id && DateOnly.FromDateTime(item.PunchTime) == day).OrderBy(item => item.PunchTime).ToList();
            var first = punches.FirstOrDefault()?.PunchTime; var last = punches.Count > 1 ? punches[^1].PunchTime : (DateTime?)null;
            if (!first.HasValue && (!employee.IsActive || day > DateOnly.FromDateTime(DateTime.Today) || day < employee.JoiningDate)) continue;
            var status = !first.HasValue ? AttendanceRules.IsWeeklyOff(day) ? "Weekly Off" : approvedLeaves.Any(x => x.EmployeeId == employee.Id && x.FromDate <= day && x.ToDate >= day) ? "Leave" : "Absent" : TimeOnly.FromDateTime(first.Value) > new TimeOnly(9, 30) ? "Late" : "Present";
            result.Add((day, first?.ToString("hh:mm tt") ?? "--", last?.ToString("hh:mm tt") ?? "--", punches.Count, status));
        }
        return result;
    }
}
