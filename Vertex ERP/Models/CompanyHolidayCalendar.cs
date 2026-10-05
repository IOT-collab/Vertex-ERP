namespace VertexERP.Models;

public sealed record CompanyHoliday(string Name, DateOnly Start, DateOnly End)
{
    public int Days => End.DayNumber - Start.DayNumber + 1;
}

// Transcribed from the signed Vertex Group Holiday List of 2026.
// Keep the Diwali period together: the source assigns three days to the group.
public sealed class CompanyHolidayCalendar
{
    public int Year => 2026;
    public DateOnly Today { get; }
    public IReadOnlyList<CompanyHoliday> Holidays { get; } = Array.AsReadOnly(new[]
    {
        Day("New Year", 1, 1),
        Day("Republic Day", 1, 26),
        Day("Holi", 3, 4),
        Day("Labour Day", 5, 1),
        Day("Independence Day", 8, 15),
        Day("Raksha Bandhan", 8, 28),
        Day("Janmashtami", 9, 4),
        Day("Gandhi Jayanti", 10, 2),
        Day("Dussehra", 10, 20),
        new CompanyHoliday("Diwali, Gowardhan Puja & Bhai Dooj", new(2026, 11, 9), new(2026, 11, 11))
    });
    public int TotalDays => Holidays.Sum(h => h.Days);
    public int RemainingDays => Holidays.Sum(h => Math.Max(0, h.End.DayNumber - Math.Max(h.Start.DayNumber, Today.DayNumber) + 1));
    public CompanyHoliday? NextHoliday => Holidays.FirstOrDefault(h => h.End >= Today);

    public CompanyHolidayCalendar(DateTimeOffset now)
    {
        Today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(5.5)).DateTime);
    }

    private static CompanyHoliday Day(string name, int month, int day) => new(name, new(2026, month, day), new(2026, month, day));
}
