using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

static class ExtraFacilityChecks
{
    public static void Run()
    {
        var passed = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); passed++; }
        var date = new DateOnly(2026, 10, 26);
        LeaveRequest Facility(int id, string type, string status = "Pending")
        {
            var request = new LeaveRequest { Id = id, EmployeeId = 7, FromDate = date, ToDate = date, Status = status, Reason = "Personal work" };
            ExtraLeaveFacility.SetDetails(request, type, "First", new(10, 0), new(12, 0));
            return request;
        }
        var half = Facility(1, ExtraLeaveFacility.HalfDay, "Approved");
        var pendingHalf = Facility(2, ExtraLeaveFacility.HalfDay);
        var shortLeave = Facility(3, ExtraLeaveFacility.ShortLeave);
        var requests = new List<LeaveRequest> { half, pendingHalf, shortLeave };
        Check(ExtraLeaveFacility.Quota(requests, 7, date, ExtraLeaveFacility.HalfDay) == new FacilityQuota(2, 1, 1), "Half-day used and pending separately counted");
        Check(ExtraLeaveFacility.Quota(requests, 7, date, ExtraLeaveFacility.HalfDay).Available == 0, "Pending reserves the last monthly slot");
        Check(ExtraLeaveFacility.Quota(requests, 7, date, ExtraLeaveFacility.ShortLeave).Available == 1, "Short leave quota independent of half day");
        Check(ExtraLeaveFacility.Quota(requests, 8, date, ExtraLeaveFacility.HalfDay).Available == 2, "Employee quotas isolated");
        Check(ExtraLeaveFacility.Quota(requests, 7, date.AddMonths(1), ExtraLeaveFacility.HalfDay).Available == 2, "Next month resets without carry forward");
        Check(ExtraLeaveFacility.Quota(requests, 7, date.AddYears(1), ExtraLeaveFacility.HalfDay).Available == 2, "Year boundaries isolated");
        Check(ExtraLeaveFacility.Quota(requests, 7, date, ExtraLeaveFacility.HalfDay, 2).Available == 1, "Edit excludes its own reservation");
        pendingHalf.Status = "Rejected";
        Check(ExtraLeaveFacility.Quota(requests, 7, date, ExtraLeaveFacility.HalfDay).Available == 1, "Rejection releases reservation");
        half.Status = "Cancelled";
        Check(ExtraLeaveFacility.Quota(requests, 7, date, ExtraLeaveFacility.HalfDay).Available == 2, "Cancellation restores used quota");
        half.Status = "Approved";
        pendingHalf.FromDate = pendingHalf.ToDate = date.AddMonths(1); pendingHalf.Status = "Pending";
        Check(ExtraLeaveFacility.Quota(requests, 7, date, ExtraLeaveFacility.HalfDay).Available == 1 && ExtraLeaveFacility.Quota(requests, 7, date.AddMonths(1), ExtraLeaveFacility.HalfDay).Available == 1, "Moving request reserves destination month only");
        requests.Remove(shortLeave);
        Check(ExtraLeaveFacility.Quota(requests, 7, date, ExtraLeaveFacility.ShortLeave).Available == 2, "Deleting pending request releases slot");
        foreach (var session in new[] { "First", "Second" }) Check(ExtraLeaveFacility.Valid("Half Day", date, date, "Work", session, null, null), session + " half accepted");
        Check(!ExtraLeaveFacility.Valid("Half Day", date, date, "Work", null, null, null), "Half session required");
        Check(!ExtraLeaveFacility.Valid("Half Day", date, date.AddDays(1), "Work", "First", null, null), "Half day must use a single date");
        Check(ExtraLeaveFacility.Valid("Short Leave", date, date, "Work", null, new(10, 0), new(12, 0)), "Exactly 2 hours accepted");
        Check(ExtraLeaveFacility.Valid("Short Leave", date, date, "Work", null, new(10, 0), new(10, 30)), "Shorter request accepted as one occasion");
        Check(!ExtraLeaveFacility.Valid("Short Leave", date, date, "Work", null, new(10, 0), new(12, 1)), "Over 2 hours rejected");
        Check(!ExtraLeaveFacility.Valid("Short Leave", date, date, "Work", null, new(10, 0), new(10, 0)), "Zero duration rejected");
        Check(!ExtraLeaveFacility.Valid("Short Leave", date, date, "Work", null, new(23, 0), new(1, 0)), "Reversed and overnight time rejected");
        Check(!ExtraLeaveFacility.Valid("Short Leave", date, date, "Work", null, null, new(12, 0)), "Missing time rejected");
        Check(!ExtraLeaveFacility.Valid("Short Leave", date, date, " ", null, new(10, 0), new(12, 0)), "Reason required");
        Check(!ExtraLeaveFacility.Valid("Short Leave", date, date, new string('x', 501), null, new(10, 0), new(12, 0)), "Reason length enforced");
        foreach (var type in new[] { ExtraLeaveFacility.HalfDay, ExtraLeaveFacility.ShortLeave })
        {
            foreach (var managerDecision in new[] { "Approved", "Rejected" })
            {
                var request = Facility(10, type);
                Check(!LeaveApprovalWorkflow.DecideFinal(request, "Approved", 3, null), type + " cannot bypass manager");
                Check(LeaveApprovalWorkflow.DecideManager(request, managerDecision, 2, "Manager", "Review") && request.Status == "Pending" && request.DisplayStatus == "Pending HR", type + " manager " + managerDecision + " forwards to HR");
                Check(!LeaveApprovalWorkflow.CanTakeFinalAction(request, false, true), "Admin is view only for " + type);
                Check(LeaveApprovalWorkflow.CanTakeFinalAction(request, true, false), "HR may decide " + type);
                Check(LeaveApprovalWorkflow.DecideFinal(request, "Approved", 3, "Paid facility") && request.ManagerDecision == managerDecision, type + " final approval preserves manager decision");
                Check(LeaveBalanceCalculator.Days(new[] { request }, 2026, "Approved") == 0, type + " does not deduct annual leave");
            }
        }
        var ordinary = new LeaveRequest { EmployeeId = 7, LeaveType = "CL", FromDate = date, ToDate = date, Status = "Approved" };
        half.LeaveType = "CL"; // Defensive exclusion even if historic/imported category text matches normal leave.
        var balance = new ManualLeaveBalance { EmployeeId = 7, Year = 2026, Category = "CL", TotalDays = 10 };
        LeaveBalanceCalculator.ApplyRequests(new[] { balance }, new[] { half, ordinary, shortLeave });
        Check(balance.AvailableDays == 9, "Facility never consumes CL entitlement even with matching category");
        Check(LeaveApprovalWorkflow.CanTakeFinalAction(ordinary, false, true), "Existing admin action for regular leave unchanged");
        var duplicate = Facility(20, "Half Day");
        Check(ExtraLeaveFacility.Overlaps(duplicate, Facility(21, "Half Day")), "Duplicate half-day session blocked");
        var second = Facility(22, "Half Day"); second.HalfDaySession = "Second";
        Check(!ExtraLeaveFacility.Overlaps(duplicate, second), "Different half sessions allowed");
        var shortCandidate = Facility(23, "Short Leave"); var otherShort = Facility(24, "Short Leave");
        Check(ExtraLeaveFacility.Overlaps(shortCandidate, otherShort), "Overlapping short slots blocked");
        otherShort.ShortLeaveFrom = new(12, 0); otherShort.ShortLeaveTo = new(13, 0);
        Check(!ExtraLeaveFacility.Overlaps(shortCandidate, otherShort), "Adjacent short slots allowed");
        Check(ExtraLeaveFacility.Overlaps(duplicate, ordinary), "Existing full-day leave blocks overlapping facility");
        var employee = new Employee { Id = 7, ReportingManagerId = 8 };
        var edited = Facility(30, "Short Leave"); edited.ManagerDecision = "Approved"; edited.ApprovalLevel = "HR/Admin";
        EmployeeRequestChanges.UpdateLeave(edited, employee, true, "Half Day", date, date, "Change", false, "Second");
        Check(edited.IsExtraFacility && edited.IsHalfDay && edited.HalfDaySession == "Second" && edited.ShortLeaveFrom == null && edited.ShortLeaveTo == null, "Edit changes facility and clears old times");
        Check(edited.ApprovalLevel == "Manager" && edited.ManagerDecision == null, "Facility edit returns to manager even for senior employee");
        EmployeeRequestChanges.UpdateLeave(edited, employee, false, "CL", date, date, "Normal leave", false);
        Check(!edited.IsExtraFacility && !edited.IsHalfDay && edited.HalfDaySession == null, "Conversion to regular leave clears facility flags");
        var approvedShort = Facility(40, "Short Leave", "Approved");
        var withoutLeave = EmployeeAttendanceReader.BuildDays(employee, date, date, Array.Empty<AttendanceLog>(), Array.Empty<LeaveRequest>()).Single();
        var withShort = EmployeeAttendanceReader.BuildDays(employee, date, date, Array.Empty<AttendanceLog>(), new[] { approvedShort }).Single();
        Check(withShort.Status == withoutLeave.Status, "Short leave does not mark whole day On Leave");
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=unused").Options);
        var model = db.Model.FindEntityType(typeof(LeaveRequest))!;
        Check(model.FindProperty("IsExtraFacility") != null && model.FindProperty("ShortLeaveFrom")!.ClrType == typeof(TimeOnly?), "Facility fields persist in EF model");
        Console.WriteLine($"{passed} extra facility checks passed.");
    }
}
