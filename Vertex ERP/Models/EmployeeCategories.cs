namespace VertexERP.Models;

public static class EmployeeCategories
{
    public const string BlueCollar = "Blue Collar";
    public const string WhiteCollar = "White Collar";
    public const string ValidationPattern = "^(Blue Collar|White Collar)$";
    public static IReadOnlyList<string> All { get; } = new[] { BlueCollar, WhiteCollar };
}
