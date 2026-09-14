using PdfSharp.Drawing;
using PdfSharp.Pdf;
using VertexERP.Models;

namespace VertexERP.Services;

public static class AdminReportPdfService
{
    public static byte[] Create(AdminReportViewModel report)
    {
        using var document = new PdfDocument();
        document.Info.Title = report.ReportTitle;
        var font = new XFont("Vertex Sans", 8, XFontStyleEx.Regular);
        var bold = new XFont("Vertex Sans", 8, XFontStyleEx.Bold);
        var title = new XFont("Vertex Sans", 17, XFontStyleEx.Bold);
        var ink = XBrushes.DarkSlateGray;
        const double left = 30, tableWidth = 782, lineHeight = 12, bottom = 554;
        var cellWidth = tableWidth / Math.Max(1, report.Columns.Count);
        XGraphics? graphics = null;
        double y = 0;
        void NewPage()
        {
            graphics?.Dispose();
            var page = document.AddPage();
            page.Width = 842; page.Height = 595;
            graphics = XGraphics.FromPdfPage(page);
            graphics.DrawString("VERTEX ERP / " + report.ReportTitle, title, ink, new XPoint(left, 34));
            graphics.DrawString($"Period: {report.FromDate:dd MMM yyyy} – {report.ToDate:dd MMM yyyy}    Records: {report.Rows.Count}", font, ink, new XPoint(left, 54));
            y = 70;
            foreach (var line in Wrap(graphics, report.FilterSummary, font, tableWidth)) { graphics.DrawString(line, font, ink, new XPoint(left, y)); y += lineHeight; }
            var headers = report.Columns.Select(c => Wrap(graphics, c, bold, cellWidth - 10)).ToList();
            var height = Math.Max(1, headers.Select(h => h.Count).DefaultIfEmpty(1).Max()) * lineHeight + 10;
            graphics.DrawRectangle(new XSolidBrush(XColor.FromArgb(26, 48, 78)), left, y, tableWidth, height);
            for (var i = 0; i < headers.Count; i++)
                for (var line = 0; line < headers[i].Count; line++) graphics.DrawString(headers[i][line], bold, XBrushes.White, new XPoint(left + i * cellWidth + 5, y + 13 + line * lineHeight));
            y += height;
            graphics.DrawString($"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC  |  Page {document.PageCount}", font, ink, new XPoint(left, 578));
        }
        NewPage();
        foreach (var row in report.Rows)
        {
            var cells = report.Columns.Select((_, i) => Wrap(graphics!, i < row.Count ? row[i] : "", font, cellWidth - 10)).ToList();
            var lines = cells.Select(c => c.Count).DefaultIfEmpty(1).Max();
            var offset = 0;
            while (offset < lines)
            {
                var available = (int)((bottom - y - 10) / lineHeight);
                if (available < 1) { NewPage(); continue; }
                var count = Math.Min(lines - offset, available);
                for (var i = 0; i < cells.Count; i++)
                    for (var n = 0; n < count && offset + n < cells[i].Count; n++)
                        graphics!.DrawString(cells[i][offset + n], font, ink, new XPoint(left + i * cellWidth + 5, y + 13 + n * lineHeight));
                y += count * lineHeight + 10;
                graphics!.DrawLine(XPens.LightGray, left, y, left + tableWidth, y);
                offset += count;
                if (offset < lines) NewPage();
            }
        }
        if (report.Rows.Count == 0) graphics!.DrawString("No records match the selected filters.", font, ink, new XPoint(left + 5, y + 25));
        graphics?.Dispose();
        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    private static List<string> Wrap(XGraphics graphics, string? text, XFont font, double width)
    {
        var lines = new List<string>();
        foreach (var paragraph in (text ?? "").Replace("\r", "").Split('\n'))
        {
            var current = "";
            foreach (var ch in paragraph)
            {
                if (current.Length > 0 && graphics.MeasureString(current + ch, font).Width > width) { lines.Add(current); current = ""; }
                current += ch;
            }
            lines.Add(current);
        }
        return lines;
    }
}
