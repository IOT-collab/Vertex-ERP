using System.Globalization;
using System.Text;
using VertexERP.Models;

namespace VertexERP.Services;

public static class AdminReportPdfService
{
    public static byte[] Create(AdminReportViewModel report)
    {
        const double width = 842, height = 595;
        var pages = new List<string>();
        var rowsPerPage = 24;
        var chunks = report.Rows.Count == 0 ? new[] { Array.Empty<IReadOnlyList<string>>() } : report.Rows.Chunk(rowsPerPage);
        foreach (var chunk in chunks)
        {
            var content = new StringBuilder();
            Text(content, 36, 558, "VERTEX ERP", 15, true);
            Text(content, 36, 537, report.ReportTitle, 12, true);
            Text(content, 806, 558, $"Generated: {DateTime.Now:dd MMM yyyy, hh:mm tt}", 7, false, true);
            Text(content, 36, 519, $"Period: {report.FromDate:dd MMM yyyy} to {report.ToDate:dd MMM yyyy}", 8);
            Text(content, 806, 519, $"Present: {report.PresentCount}   Absent: {report.AbsentCount}   Late: {report.LateCount}", 8, true, true);
            content.Append("0.20 0.36 0.70 RG 1 w 36 508 m 806 508 l S\n");

            var columns = Math.Max(1, report.Columns.Count);
            var cellWidth = 770d / columns;
            content.Append("0.06 0.11 0.20 rg 36 486 770 20 re f\n");
            for (var i = 0; i < report.Columns.Count; i++) Text(content, 40 + i * cellWidth, 493, Trim(report.Columns[i], 25), 7, true, false, "1 1 1");
            var y = 470d;
            foreach (var row in chunk)
            {
                content.Append("0.82 0.86 0.92 RG .35 w 36 " + (y - 5).ToString("0.##", CultureInfo.InvariantCulture) + " m 806 " + (y - 5).ToString("0.##", CultureInfo.InvariantCulture) + " l S\n");
                for (var i = 0; i < report.Columns.Count; i++) Text(content, 40 + i * cellWidth, y, Trim(i < row.Count ? row[i] : "", Math.Max(10, (int)(cellWidth / 4.2))), 6.6);
                y -= 18;
            }
            if (report.Rows.Count == 0) Text(content, width / 2, 450, "No records found for the selected filters.", 10, false, true);
            pages.Add(content.ToString());
        }
        return BuildPdf(pages, width, height);
    }

    private static void Text(StringBuilder content, double x, double y, string value, double size, bool bold = false, bool right = false, string colour = "0.10 0.15 0.24")
    {
        if (right) x -= value.Length * size * .48;
        content.Append($"{colour} rg BT /{(bold ? "F2" : "F1")} {size:0.##} Tf {x:0.##} {y:0.##} Td ({Escape(value)}) Tj ET\n");
    }
    private static string Trim(string value, int max) => value.Length <= max ? value : value[..Math.Max(1, max - 3)] + "...";
    private static string Escape(string value) => (value ?? "").Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Replace("\r", " ").Replace("\n", " ");
    private static byte[] BuildPdf(IReadOnlyList<string> pages, double width, double height)
    {
        var objects = new List<byte[]>();
        objects.Add(Bytes("<< /Type /Catalog /Pages 2 0 R >>"));
        var pageIds = Enumerable.Range(0, pages.Count).Select(i => 5 + i * 2).ToArray();
        objects.Add(Bytes($"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(id => $"{id} 0 R"))}] /Count {pages.Count} >>"));
        objects.Add(Bytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));
        objects.Add(Bytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>"));
        foreach (var page in pages)
        {
            var streamId = objects.Count + 2;
            objects.Add(Bytes($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {streamId} 0 R >>"));
            objects.Add(Stream(Bytes(page)));
        }
        using var output = new MemoryStream(); void Write(string value) { var data = Bytes(value); output.Write(data); }
        Write("%PDF-1.4\n"); var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Count; i++) { offsets.Add(output.Position); Write($"{i + 1} 0 obj\n"); output.Write(objects[i]); Write("\nendobj\n"); }
        var xref = output.Position; Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"); foreach (var offset in offsets.Skip(1)) Write($"{offset:0000000000} 00000 n \n"); Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
        return output.ToArray();
    }
    private static byte[] Stream(byte[] data) => Bytes($"<< /Length {data.Length} >>\nstream\n").Concat(data).Concat(Bytes("\nendstream")).ToArray();
    private static byte[] Bytes(string value) => Encoding.ASCII.GetBytes(value);
}
