using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Controllers;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

static class LeaveCalculatorChecks
{
    public static void Run()
    {
        var count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); count++; }
        foreach (var (days, el, cl) in new[] { (0m,0m,0m), (10m,0m,0m), (19.5m,0m,0m), (20m,1m,0m), (39.5m,1m,0m), (40m,2m,1m), (95m,4m,2m) })
        {
            var months = new[] { new LeaveMonth { WorkingDays = days } };
            Check(LeaveBalanceCalculator.Calculate(months,"EL").Earned == el, $"EL threshold {days}");
            Check(LeaveBalanceCalculator.Calculate(months,"CL").Earned == cl && LeaveBalanceCalculator.Calculate(months,"ML").Earned == cl, $"CL/ML threshold {days}");
        }
        var entries = new[] { new LeaveMonth { WorkingDays=25, AdjustedEL=.5m, EarnedEW=2 }, new LeaveMonth { WorkingDays=15, AdjustedEL=1, EarnedEW=.5m, AdjustedEW=.5m } };
        Check(LeaveBalanceCalculator.Calculate(entries,"EL") == (2m,1.5m), "Annual cumulative days retain monthly remainder");
        Check(LeaveBalanceCalculator.Calculate(entries,"EW") == (2.5m,.5m), "EW manual earning and adjustment");
        var approved = new LeaveRequest { EmployeeId=1, LeaveType="EL", FromDate=new(2026,12,31), ToDate=new(2027,1,2), Status="Approved" };
        var half = new LeaveRequest { EmployeeId=1, LeaveType="EL", FromDate=new(2026,2,3), ToDate=new(2026,2,3), Status="Approved", IsHalfDay=true };
        var pending = new LeaveRequest { EmployeeId=1, LeaveType="EL", FromDate=new(2026,3,1), ToDate=new(2026,3,2), Status="Pending" };
        var rejected = new LeaveRequest { EmployeeId=1, LeaveType="EL", FromDate=new(2026,4,1), ToDate=new(2026,4,3), Status="Rejected" };
        var otherEmployee = new LeaveRequest { EmployeeId=2, LeaveType="EL", FromDate=new(2026,5,1), ToDate=new(2026,5,3), Status="Approved" };
        var requests = new[] { approved, half, pending, rejected, otherEmployee };
        var balance = new ManualLeaveBalance { EmployeeId=1, Year=2026, Category="EL", TotalDays=7, UsedDays=.5m };
        LeaveBalanceCalculator.ApplyRequests(new[]{balance}, requests);
        Check(balance.ApprovedDays==1.5m && balance.EffectiveUsedDays==2 && balance.AvailableDays==5, "Category, employee, half-day and year-scoped approval usage");
        Check(balance.PendingDays==2, "Pending reported separately without deduction");
        LeaveBalanceCalculator.ApplyRequests(new[]{balance}, requests);
        Check(balance.AvailableDays==5, "Repeated reconciliation is idempotent");
        Check(LeaveBalanceCalculator.Days(new[]{approved},2027,"Approved")==2, "Cross-year allocation");
        approved.Status="Cancelled";
        LeaveBalanceCalculator.ApplyRequests(new[]{balance},requests);
        Check(balance.AvailableDays==6, "Approved cancellation restores only its year's days");
        half.Status="Rejected";
        LeaveBalanceCalculator.ApplyRequests(new[]{balance},requests);
        Check(balance.AvailableDays==6.5m, "Reversed decision restores half day");
        Check(LeaveBalanceCalculator.Days(new[]{pending,pending},2026,"Pending")==2, "Overlapping duplicate dates count once");
        balance.TotalDays=0;
        Check(balance.AvailableDays==-.5m, "Deficit remains visible");
        var unfilled = new List<ManualLeaveBalance>();
        LeaveBalanceCalculator.IncludeUnfilled(unfilled, new[]{pending}, 2026);
        Check(unfilled.Single().Revision==0 && unfilled.Single().PendingDays==2, "Requests remain visible before HR enters entitlement");
        LeaveBalanceCalculator.IncludeUnfilled(unfilled, new[]{pending}, 2026);
        Check(unfilled.Count==1, "Unfilled category creation is idempotent");
        var carried = new LeaveCalculation { OpeningEL = 4, OpeningCL = 2.5m, OpeningML = 3, OpeningEW = 1 };
        Check(LeaveBalanceCalculator.Opening(null, "CL") == 0 && LeaveBalanceCalculator.Opening(new LeaveCalculation(), "ML") == 0, "Existing calculations default CL/ML opening to zero");
        foreach (var category in new[] { "CL", "ML" })
        {
            var opening = LeaveBalanceCalculator.Opening(carried, category);
            var earned = LeaveBalanceCalculator.Calculate(entries, category).Earned;
            var carriedBalance = new ManualLeaveBalance { EmployeeId = 1, Year = 2026, Category = category, TotalDays = opening + earned, UsedDays = .5m };
            var categoryRequest = new LeaveRequest { EmployeeId = 1, LeaveType = category, FromDate = new(2026, 2, 3), ToDate = new(2026, 2, 3), Status = "Approved", IsHalfDay = true };
            LeaveBalanceCalculator.ApplyRequests(new[] { carriedBalance }, new[] { categoryRequest });
            Check(carriedBalance.AvailableDays == opening, category + " includes carry forward and earned less approved and additional usage");
            carriedBalance.TotalDays = LeaveBalanceCalculator.Opening(carried, category) + earned;
            LeaveBalanceCalculator.ApplyRequests(new[] { carriedBalance }, new[] { categoryRequest });
            Check(carriedBalance.AvailableDays == opening, category + " repeated save does not duplicate carry forward");
        }
        var roles=typeof(LeaveBalancesController).GetCustomAttributes(typeof(AuthorizeAttribute),true).Cast<AuthorizeAttribute>().Single().Roles;
        Check(roles=="Admin,HR", "Calculator limited to HR and Admin");
        Check(typeof(LeaveBalancesController).GetMethods().Where(x=>x.Name=="Calculate" && x.IsDefined(typeof(HttpPostAttribute),true)).Single().IsDefined(typeof(ValidateAntiForgeryTokenAttribute),true), "Calculator POST requires antiforgery");
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=unused").Options);
        var entity=db.Model.FindEntityType(typeof(LeaveCalculation))!;
        Check(entity.GetIndexes().Any(x=>x.IsUnique && x.Properties.Select(p=>p.Name).SequenceEqual(new[]{"EmployeeId","Year"})), "One calculator per employee/year");
        Check(entity.FindProperty("OpeningCL")!.GetPrecision() == 10 && entity.FindProperty("OpeningML")!.GetScale() == 2, "CL/ML opening balances preserve half-day precision");
        Check(entity.FindProperty("Revision")!.IsConcurrencyToken, "Calculator rejects concurrent stale updates");
        Check(db.Model.FindEntityType(typeof(ManualLeaveBalance))!.FindProperty("ApprovedDays")==null, "Approved usage is derived, never stale persisted state");
        Console.WriteLine($"{count} leave calculator checks passed.");
    }
}
