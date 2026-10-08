using System.IO.Compression;
using System.Xml.Linq;
using VertexERP.Models;

namespace VertexERP.Services;

public static class AdminReportExcelService
{
    public static byte[] CreateEmployeeList(IEnumerable<Employee> employees)
    {
        var data = new List<IReadOnlyList<string>>
        {
            new[] { "Employee ID", "Full Name", "Mobile Number", "Aadhaar Number", "Department", "Reporting Manager" }
        };
        data.AddRange(employees.Select(employee => new[]
        {
            employee.EmployeeCode, employee.FullName, employee.PhoneNumber,
            employee.AadhaarNumber ?? "", employee.Department, employee.ReportingManager?.FullName ?? ""
        }));
        return CreateWorkbook(data, 6, 1, "Employees");
    }

    private static byte[] CreateWorkbook(IReadOnlyList<IReadOnlyList<string>> data, int columnCount, int frozenRows, string sheetName)
    {
        XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace p = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace c = "http://schemas.openxmlformats.org/package/2006/content-types";
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            void Add(string path, XElement xml) { using var entry = zip.CreateEntry(path).Open(); new XDocument(xml).Save(entry); }
            Add("[Content_Types].xml", new XElement(c + "Types",
                new XElement(c + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(c + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                new XElement(c + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                new XElement(c + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"))));
            XElement Relation(string id, string type, string target) => new(p + "Relationship", new XAttribute("Id", id), new XAttribute("Type", r.NamespaceName + "/" + type), new XAttribute("Target", target));
            Add("_rels/.rels", new XElement(p + "Relationships", Relation("rId1", "officeDocument", "xl/workbook.xml")));
            Add("xl/workbook.xml", new XElement(s + "workbook", new XElement(s + "sheets", new XElement(s + "sheet", new XAttribute("name", sheetName), new XAttribute("sheetId", "1"), new XAttribute(r + "id", "rId1")))));
            Add("xl/_rels/workbook.xml.rels", new XElement(p + "Relationships", Relation("rId1", "worksheet", "worksheets/sheet1.xml")));
            // Inline strings preserve leading zeros and never execute spreadsheet formulas.
            var sheetData = new XElement(s + "sheetData", data.Select((row, i) => new XElement(s + "row", new XAttribute("r", i + 1), row.Select(value => new XElement(s + "c", new XAttribute("t", "inlineStr"), new XElement(s + "is", new XElement(s + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), Clean(value))))))));
            Add("xl/worksheets/sheet1.xml", new XElement(s + "worksheet",
                new XElement(s + "sheetViews", new XElement(s + "sheetView", new XAttribute("workbookViewId", "0"), new XElement(s + "pane", new XAttribute("ySplit", frozenRows), new XAttribute("topLeftCell", $"A{frozenRows + 1}"), new XAttribute("state", "frozen")))),
                new XElement(s + "cols", new XElement(s + "col", new XAttribute("min", "1"), new XAttribute("max", Math.Max(1, columnCount)), new XAttribute("width", "24"), new XAttribute("customWidth", "1"))), sheetData));
        }
        return stream.ToArray();
    }
    private static string Clean(string? value) => string.Concat((value ?? "").Where(ch => !char.IsControl(ch) || ch is '\n' or '\r' or '\t'));
}
