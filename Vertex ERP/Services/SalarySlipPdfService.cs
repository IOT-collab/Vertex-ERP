using System.Globalization;
using System.Text;
using VertexERP.Models;

namespace VertexERP.Services;

public static class SalarySlipPdfService
{
    private const double PageWidth = 597;
    private const double PageHeight = 600;

    public static byte[] Create(Employee employee, EmployeeSalaryDetail salary, EmployeeBankDetail? bank, int year, int month)
    {
        var templatePath = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "templates", "salary-slip-master.jpg"),
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "templates", "salary-slip-master.jpg"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot", "templates", "salary-slip-master.jpg"))
        }.FirstOrDefault(File.Exists) ?? string.Empty;
        var template = File.Exists(templatePath) ? File.ReadAllBytes(templatePath) : Array.Empty<byte>();
        var label = new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
        var content = new StringBuilder();

        // The approved Aug Salary PDF is the full-page visual master.
        if (template.Length > 0) content.Append("q 597 0 0 600 0 0 cm /Im1 Do Q\n");
        void Fill(double x, double y, double w, double h, string colour = "1 1 1") => content.Append($"{colour} rg {x} {y} {w} {h} re f\n");
        void Text(double x, double y, string value, double size = 7.5, bool bold = false, string align = "L")
        {
            var width = ApproxWidth(value, size, bold);
            var drawX = align == "R" ? x - width : align == "C" ? x - width / 2 : x;
            content.Append($"0 0 0 rg BT /{(bold ? "F2" : "F1")} {size:0.##} Tf {drawX:0.##} {y:0.##} Td ({Escape(value)}) Tj ET\n");
        }
        void Line(double x1, double y1, double x2, double y2, double width = .65) => content.Append($"0.39 0.47 0.60 RG {width} w {x1} {y1} m {x2} {y2} l S\n");
        void Box(double x, double y, double w, double h) { Line(x, y, x + w, y); Line(x, y + h, x + w, y + h); Line(x, y, x, y + h); Line(x + w, y, x + w, y + h); }

        // Remove every sample value baked into the approved reference. Only its branded
        // company header and CIN footer remain; the complete payslip body is drawn live.
        Fill(0, 34, PageWidth, 491);

        const double left = 56.7, right = 540.3, width = right - left;
        content.Append("0.027 0.063 0.184 rg 56.7 491 483.6 32 re f\n");
        content.Append($"1 1 1 rg BT /F2 12 Tf {CenteredX($"PAYSLIP FOR THE MONTH OF {label}", 12, true):0.##} 502 Td ({Escape($"PAYSLIP FOR THE MONTH OF {label}")}) Tj ET\n");

        var infoTop = 475d; var infoBottom = 380d; Box(left, infoBottom, width, infoTop - infoBottom); Line(303, infoBottom, 303, infoTop);
        var leftInfo = new[] { ("Name", employee.FullName), ("Joining Date", employee.JoiningDate.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture)), ("Designation", employee.Designation), ("Department", employee.Department), ("Effective Work Days", DateTime.DaysInMonth(year, month).ToString()), ("LOP", "0") };
        var rightInfo = new[] { ("Employee No.", employee.EmployeeCode), ("Bank Name", bank?.BankName ?? "--"), ("Bank Account No.", bank == null ? "--" : $"XXXX XXXX {bank.AccountLastFour}"), ("PAN Number", bank?.PanNumber ?? "--"), ("PF No.", salary.PfNumber ?? "--"), ("PF UAN", salary.PfUan ?? bank?.UanNumber ?? "--") };
        for (var i = 0; i < 6; i++) { var y = 464 - i * 15.7; Text(63, y, leftInfo[i].Item1); Text(159, y, leftInfo[i].Item2, 7.7, true); Text(309, y, rightInfo[i].Item1); Text(410, y, rightInfo[i].Item2, 7.7, true); }

        var tableTop = 372d; var rowH = 19.5; var tableBottom = tableTop - rowH * 6; var columns = new[] { left, 184.5, 235.7, 295.3, 445.3, 540.3 };
        Fill(left, tableTop - rowH, width, rowH, "0.91 0.94 0.98"); Box(left, tableBottom, width, tableTop - tableBottom);
        foreach (var x in columns.Skip(1).SkipLast(1)) Line(x, tableBottom, x, tableTop);
        for (var i = 1; i < 6; i++) Line(left, tableTop - rowH * i, right, tableTop - rowH * i);
        Text((columns[0] + columns[1]) / 2, 359, "EARNINGS", 8, true, "C"); Text((columns[1] + columns[2]) / 2, 359, "MASTER", 8, true, "C"); Text((columns[2] + columns[3]) / 2, 359, "ACTUAL", 8, true, "C"); Text((columns[3] + columns[4]) / 2, 359, "DEDUCTIONS", 8, true, "C"); Text((columns[4] + columns[5]) / 2, 359, "ACTUAL", 8, true, "C");
        var rows = new[] { ("BASIC", salary.BasicSalary, "PROVIDENT FUND", salary.ProvidentFund), ("HRA", salary.HouseRentAllowance, "PROFESSIONAL TAX", salary.ProfessionalTax), ("CONVEYANCE ALLOWANCE", salary.ConveyanceAllowance, "TDS", salary.Tds), ("SPECIAL ALLOWANCE", salary.SpecialAllowance, "OTHER DEDUCTIONS", salary.OtherDeductions) };
        for (var i = 0; i < 4; i++) { var y = 339 - i * rowH; Text(64, y, rows[i].Item1, 7.2); Text(229, y, Money(rows[i].Item2), 7.2, false, "R"); Text(289, y, Money(rows[i].Item2), 7.2, false, "R"); Text(302, y, rows[i].Item3, 7.2); Text(531, y, Money(rows[i].Item4), 7.2, false, "R"); }
        Text(64, 261, "TOTAL EARNINGS: INR", 7.1, true); Text(229, 261, Money(salary.GrossSalary), 7.1, true, "R"); Text(289, 261, Money(salary.GrossSalary), 7.1, true, "R"); Text(302, 261, "TOTAL DEDUCTIONS: INR", 7.1, true); Text(531, 261, Money(salary.TotalDeductions), 7.1, true, "R");

        Text(63, 218, "Net Pay for the month:", 9); Text(198, 218, $"INR {Money(salary.NetSalary)}", 10, true);
        Text(63, 197, $"({AmountInWords(salary.NetSalary)} Only)", 8, true);
        Line(left, 171, right, 171); Text(PageWidth / 2, 154, "This is a system-generated payslip and does not require a signature.", 7.3, false, "C");
        return BuildPdf(content.ToString(), template);
    }

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);
    private static string AmountInWords(decimal value) => $"Indian Rupees {IntegerWords((long)Math.Floor(value))}";
    private static string IntegerWords(long value)
    {
        if (value == 0) return "Zero";
        string[] ones = { "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen" };
        string[] tens = { "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety" };
        string UnderThousand(long number)
        {
            var words = new List<string>();
            if (number >= 100) { words.Add(ones[number / 100]); words.Add("Hundred"); number %= 100; }
            if (number >= 20) { words.Add(tens[number / 10]); number %= 10; }
            if (number > 0) words.Add(ones[number]);
            return string.Join(" ", words);
        }
        var parts = new List<string>();
        if (value >= 10_000_000) { parts.Add(UnderThousand(value / 10_000_000)); parts.Add("Crore"); value %= 10_000_000; }
        if (value >= 100_000) { parts.Add(UnderThousand(value / 100_000)); parts.Add("Lakh"); value %= 100_000; }
        if (value >= 1_000) { parts.Add(UnderThousand(value / 1_000)); parts.Add("Thousand"); value %= 1_000; }
        if (value > 0) parts.Add(UnderThousand(value));
        return string.Join(" ", parts);
    }
    private static double ApproxWidth(string value, double size, bool bold) => value.Length * size * (bold ? .54 : .50);
    private static double CenteredX(string value, double size, bool bold) => PageWidth / 2 - ApproxWidth(value, size, bold) / 2;
    private static string Escape(string value) => (value ?? "").Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    private static byte[] BuildPdf(string stream, byte[] image)
    {
        var imageResource = image.Length > 0 ? "/XObject << /Im1 7 0 R >>" : "";
        var objects = new List<byte[]> {
            Bytes("<< /Type /Catalog /Pages 2 0 R >>"), Bytes("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Bytes($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] /Resources << /Font << /F1 5 0 R /F2 6 0 R >> {imageResource} >> /Contents 4 0 R >>"),
            Stream(Bytes(stream)), Bytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"), Bytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>")
        };
        if (image.Length > 0) objects.Add(Stream(image, " /Type /XObject /Subtype /Image /Width 1194 /Height 1200 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode"));
        using var output = new MemoryStream(); void Write(string text) { var data = Bytes(text); output.Write(data); }
        Write("%PDF-1.4\n"); var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Count; i++) { offsets.Add(output.Position); Write($"{i + 1} 0 obj\n"); output.Write(objects[i]); Write("\nendobj\n"); }
        var xref = output.Position; Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"); foreach (var offset in offsets.Skip(1)) Write($"{offset:0000000000} 00000 n \n"); Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF"); return output.ToArray();
    }
    private static byte[] Stream(byte[] data, string extra = "") { using var memory = new MemoryStream(); var header = Bytes($"<< /Length {data.Length}{extra} >>\nstream\n"); memory.Write(header); memory.Write(data); memory.Write(Bytes("\nendstream")); return memory.ToArray(); }
    private static byte[] Bytes(string value) => Encoding.ASCII.GetBytes(value);
}
