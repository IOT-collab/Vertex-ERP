namespace VertexERP.Services;

public static class AttendanceUiPresentation
{
    public static string Source(string? source) => string.IsNullOrWhiteSpace(source) ? "--"
        : string.Join(" + ", source.Split(" + ", StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value is "Manual Approved" or "Biometric / Thumb" ? "Punch" : value).Distinct());
}
