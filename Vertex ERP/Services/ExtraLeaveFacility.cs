using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VertexERP.Data;
using VertexERP.Models;

namespace VertexERP.Services;

public sealed record FacilityQuota(int Limit, int Used, int Pending)
{
    public int Available => Math.Max(0, Limit - Used - Pending);
}

public static class ExtraLeaveFacility
{
    public const string HalfDay = "Half Day";
    public const string ShortLeave = "Short Leave";
    public const int MonthlyLimit = 2;

    public static bool IsFacilityType(string? type) => type is HalfDay or ShortLeave;
    public static bool IsActive(LeaveRequest request) => request.Status is "Pending" or "Approved";

    public static FacilityQuota Quota(IEnumerable<LeaveRequest> requests, int employeeId, DateOnly date, string type, int? excludingId = null)
    {
        var matches = requests.Where(x => x.EmployeeId == employeeId && x.IsExtraFacility && x.LeaveType == type
            && x.FromDate.Year == date.Year && x.FromDate.Month == date.Month && x.Id != excludingId).ToList();
        return new(MonthlyLimit, matches.Count(x => x.Status == "Approved"), matches.Count(x => x.Status == "Pending"));
    }

    public static bool Valid(string type, DateOnly from, DateOnly to, string? reason, string? session, TimeOnly? start, TimeOnly? end) =>
        IsFacilityType(type) && from != default && from == to && !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= 500
        && (type == HalfDay ? LeaveApprovalWorkflow.ValidHalfDaySession(true, session)
            : start.HasValue && end.HasValue && end > start && end.Value - start.Value <= TimeSpan.FromHours(2));

    public static bool Overlaps(LeaveRequest candidate, LeaveRequest existing)
    {
        if (!IsActive(existing) || existing.EmployeeId != candidate.EmployeeId || existing.Id == candidate.Id && candidate.Id != 0
            || existing.FromDate > candidate.ToDate || existing.ToDate < candidate.FromDate) return false;
        // Same date/session cannot consume the facility twice. Different halves or non-overlapping short slots are allowed.
        if (candidate.IsHalfDay && existing.IsHalfDay)
            return existing.HalfDaySession == null || candidate.HalfDaySession == existing.HalfDaySession;
        if (candidate.LeaveType == ShortLeave && existing.LeaveType == ShortLeave)
            return candidate.ShortLeaveFrom < existing.ShortLeaveTo && existing.ShortLeaveFrom < candidate.ShortLeaveTo;
        return !candidate.IsExtraFacility || !existing.IsExtraFacility;
    }

    public static void SetDetails(LeaveRequest request, string type, string? session, TimeOnly? start, TimeOnly? end)
    {
        request.IsExtraFacility = IsFacilityType(type);
        request.LeaveType = type;
        request.IsHalfDay = type == HalfDay;
        request.HalfDaySession = type == HalfDay ? session : null;
        request.ShortLeaveFrom = type == ShortLeave ? start : null;
        request.ShortLeaveTo = type == ShortLeave ? end : null;
    }

    // All applications and edits for an employee share a PostgreSQL transaction lock.
    // This prevents simultaneous browser submissions from both claiming the last monthly slot.
    public static async Task<IDbContextTransaction> LockAsync(ApplicationDbContext db, int employeeId)
    {
        var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(78234, {employeeId})");
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    public static async Task<string?> ApplicationError(ApplicationDbContext db, LeaveRequest candidate)
    {
        var first = new DateOnly(candidate.FromDate.Year, candidate.FromDate.Month, 1);
        var last = first.AddMonths(1);
        var requests = await db.LeaveRequests.AsNoTracking().Where(x => x.EmployeeId == candidate.EmployeeId
            && x.FromDate < last && x.ToDate >= first && (x.Status == "Pending" || x.Status == "Approved") && x.Id != candidate.Id).ToListAsync();
        if (Quota(requests, candidate.EmployeeId, candidate.FromDate, candidate.LeaveType).Available == 0)
            return $"Your {candidate.FromDate:MMMM yyyy} {candidate.LeaveType} quota is full (2 per month, including pending requests).";
        if (requests.Any(x => Overlaps(candidate, x))) return "An approved or pending request already covers this date/session or time slot.";
        return null;
    }
}
