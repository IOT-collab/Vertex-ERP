using System.Text.Json;
using VertexERP.Models;

namespace VertexERP.Services;

public static class SalaryDayCalculation
{
    public static readonly string[] Earnings = ["BasicSalary", "HouseRentAllowance", "ConveyanceAllowance", "SpecialAllowance"];

    public static bool CanGenerate(int year, int month) =>
        CanGenerate(year, month, FieldAttendanceClock.Now);

    public static bool CanGenerate(int year, int month, DateTime indiaNow)
    {
        var previous = indiaNow.AddMonths(-1);
        return year == previous.Year && month == previous.Month;
    }

    public static Dictionary<string, decimal> MonthlyEarnings(GeneratedSalarySlip slip) =>
        string.IsNullOrEmpty(slip.MonthlyEarningsJson)
            ? Earnings.ToDictionary(f => f, f => (decimal)typeof(GeneratedSalarySlip).GetProperty(f)!.GetValue(slip)!)
            : JsonSerializer.Deserialize<Dictionary<string, decimal>>(slip.MonthlyEarningsJson)!;

    public static Dictionary<string, decimal> EditingValues(GeneratedSalarySlip slip)
    {
        var values = SalarySlipRevisionViewModel.Snapshot(slip);
        foreach (var pair in MonthlyEarnings(slip)) values[pair.Key] = pair.Value;
        return values;
    }

    // Inputs are full-month earnings; persist the baseline so repeated day edits
    // never prorate an already reduced amount. Regular deductions remain explicit amounts.
    public static void Apply(GeneratedSalarySlip slip, IReadOnlyDictionary<string, decimal> monthlyEarnings)
    {
        var totalDays = DateTime.DaysInMonth(slip.Year, slip.Month);
        if (slip.SalaryDays is not decimal days || days < 0 || days > totalDays || decimal.Round(days, 2) != days)
            throw new ArgumentException("Enter valid salary days.");
        var monthly = Earnings.ToDictionary(f => f, f => monthlyEarnings[f]);
        if (monthly.Values.Any(v => v < 0 || v > 100000000m)) throw new ArgumentException("Invalid monthly earnings.");
        slip.MonthlyEarningsJson = JsonSerializer.Serialize(monthly);
        foreach (var pair in monthly)
            typeof(GeneratedSalarySlip).GetProperty(pair.Key)!.SetValue(slip,
                decimal.Round(pair.Value * days / totalDays, 2, MidpointRounding.AwayFromZero));
    }
}
