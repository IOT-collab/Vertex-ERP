using VertexERP.Models;

void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
CompanyHolidayCalendar At(string timestamp) => new(DateTimeOffset.Parse(timestamp));
var calendar = At("2026-01-01T00:00:00+05:30");
Check(calendar.TotalDays == 12 && calendar.Holidays.Count == 10, "Circular contains 12 days across 10 occasions.");
Check(calendar.Holidays.Single(h => h.Name == "Holi").Start == new DateOnly(2026, 3, 4), "Mixed date format: Holi is 4 March.");
Check(calendar.Holidays.Single(h => h.Name == "Janmashtami").Start.DayOfWeek == DayOfWeek.Friday, "Janmashtami weekday must be calculated from its date.");
Check(At("2026-10-01T18:29:59Z").Today == new DateOnly(2026, 10, 1), "Before India midnight.");
Check(At("2026-10-01T18:30:00Z").NextHoliday?.Name == "Gandhi Jayanti", "India midnight is a holiday today.");
Check(At("2026-10-01T00:00:00+05:30").RemainingDays == 5, "Five holiday days remain on October 1.");
Check(At("2026-11-10T00:00:00+05:30").RemainingDays == 2, "Count only remaining days within Diwali period.");
Check(At("2026-11-11T00:00:00+05:30").NextHoliday?.Days == 3, "Diwali remains current on its final day.");
Check(At("2026-11-12T00:00:00+05:30").NextHoliday == null, "No upcoming holidays after the final date.");
Check(At("2027-01-01T00:00:00+05:30").RemainingDays == 0, "No stale remaining holidays in a later year.");
Console.WriteLine("Passed 10 company holiday checks.");
