namespace VertexERP.Services;

// AttendanceLogs.PunchTime stores local wall-clock time without a time zone.
// Keep field punches on the same India calendar day even on a UTC server.
public static class FieldAttendanceClock
{
    public static DateTime IndiaTime(DateTimeOffset utc) =>
        utc.ToOffset(TimeSpan.FromMinutes(330)).DateTime;

    public static DateTime Now => IndiaTime(DateTimeOffset.UtcNow);
}
