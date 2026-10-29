using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;
namespace VertexERP.Services;

public static class HrLeaveApplications
{
    public static bool Valid(HrLeaveApplication form) => form.EmployeeId>0 && form.FromDate.HasValue && form.ToDate.HasValue
        && form.FromDate.Value.Year is >=2000 and <=2100 && form.ToDate.Value.Year is >=2000 and <=2100
        && form.ToDate.Value.DayNumber - form.FromDate.Value.DayNumber is >=0 and <366
        && (ExtraLeaveFacility.IsFacilityType(form.LeaveType)
            ? ExtraLeaveFacility.Valid(form.LeaveType,form.FromDate.Value,form.ToDate.Value,form.Reason,form.HalfDaySession,form.ShortLeaveFrom,form.ShortLeaveTo)
            : LeaveApprovalWorkflow.ValidApplication(form.LeaveType,form.FromDate.Value,form.ToDate.Value,form.Reason,false));

    public static async Task<string?> ErrorAsync(ApplicationDbContext db,LeaveRequest candidate)
    {
        var requests=await db.LeaveRequests.AsNoTracking().Where(x=>x.EmployeeId==candidate.EmployeeId && x.Id!=candidate.Id
            && (x.Status=="Pending" || x.Status=="Approved") && x.FromDate.Year<=candidate.ToDate.Year && x.ToDate.Year>=candidate.FromDate.Year).ToListAsync();
        var balances=candidate.IsExtraFacility ? new List<ManualLeaveBalance>() : await db.ManualLeaveBalances.AsNoTracking()
            .Where(x=>x.EmployeeId==candidate.EmployeeId && x.Year>=candidate.FromDate.Year && x.Year<=candidate.ToDate.Year && x.Category==candidate.LeaveType).ToListAsync();
        return Error(candidate,requests,balances);
    }
    public static string? Error(LeaveRequest candidate,IEnumerable<LeaveRequest> requests,IEnumerable<ManualLeaveBalance> balances)
    {
        var existing=requests.Where(x=>x.EmployeeId==candidate.EmployeeId && ExtraLeaveFacility.IsActive(x) && (candidate.Id==0 || x.Id!=candidate.Id)).ToList();
        if(existing.Any(x=>ExtraLeaveFacility.Overlaps(candidate,x))) return "An approved or pending leave already covers these dates/session/time. Check the existing request.";
        if(candidate.IsExtraFacility)
            return ExtraLeaveFacility.Quota(existing,candidate.EmployeeId,candidate.FromDate,candidate.LeaveType).Available<1 ? "The monthly extra paid facility limit is already used or reserved by pending requests (2 per month)." : null;
        var matching=existing.Where(x=>!x.IsExtraFacility && LeaveBalanceCalculator.Category(x.LeaveType)==candidate.LeaveType).ToList();
        foreach(var year in Enumerable.Range(candidate.FromDate.Year,candidate.ToDate.Year-candidate.FromDate.Year+1))
        {
            var balance=balances.SingleOrDefault(x=>x.EmployeeId==candidate.EmployeeId && x.Year==year && x.Category==candidate.LeaveType);
            if(balance==null) return $"Please calculate or enter {candidate.LeaveType} leave balance for {year} before applying.";
            var days=Math.Min(candidate.ToDate.DayNumber,new DateOnly(year,12,31).DayNumber)-Math.Max(candidate.FromDate.DayNumber,new DateOnly(year,1,1).DayNumber)+1;
            var available=balance.TotalDays-balance.UsedDays-LeaveBalanceCalculator.Days(matching,year,"Approved")-LeaveBalanceCalculator.Days(matching,year,"Pending");
            if(available<days) return $"Insufficient {candidate.LeaveType} balance in {year}. Available after pending requests: {Math.Max(0,available):0.##}; required: {days}.";
        }
        return null;
    }
}
