namespace VertexERP.Models;

public sealed record SalarySlipMonth(int Year, int Month, string Label, bool IsReleased);

public sealed class SalarySlipPageViewModel
{
    public Employee Employee { get; init; } = null!;
    public IReadOnlyList<SalarySlipMonth> Months { get; init; } = Array.Empty<SalarySlipMonth>();
}
