using System.ComponentModel.DataAnnotations;

namespace VertexERP.Models;

public sealed class RecruitmentHiringRecord
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }
    public Department Department { get; set; } = null!;
    [Range(2020, 2100)] public int Year { get; set; }
    [Range(1, 12)] public int Month { get; set; }
    [Range(1, 5)] public int WeekNumber { get; set; }
    [Range(0, 10000)] public int TargetHires { get; set; }
    [Range(0, 10000)] public int ActualHires { get; set; }
    [MaxLength(500)] public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class RecruitmentTrackerViewModel
{
    public int Year { get; init; }
    public int Month { get; init; }
    public IReadOnlyList<Department> Departments { get; init; } = Array.Empty<Department>();
    public IReadOnlyList<RecruitmentHiringRecord> Records { get; init; } = Array.Empty<RecruitmentHiringRecord>();
    public int TotalTarget => Records.Sum(x => x.TargetHires);
    public int TotalHired => Records.Sum(x => x.ActualHires);
    public int Remaining => Math.Max(0, TotalTarget - TotalHired);
    public int Achievement => TotalTarget == 0 ? 0 : (int)Math.Round(TotalHired * 100d / TotalTarget);
}
