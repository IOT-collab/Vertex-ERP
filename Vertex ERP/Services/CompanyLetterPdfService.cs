using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using VertexERP.Models;

namespace VertexERP.Services;

/// <summary>Renders imported source content on the ERP's existing stationery.</summary>
public sealed class CompanyLetterPdfService(string contentRoot)
{
    public static readonly string[] DocumentTypes = ["Joining Letter", "Offer Letter", "Letter of Intent (LOI)", "Relieving Letter", "Experience Letter", "Increment / Promotion Letter"];
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    public static string TemplateKind(string type) => type switch
    {
        "Joining Letter" => "Joining", "Offer Letter" => "Offer", "Letter of Intent (LOI)" => "LOI",
        "Relieving Letter" or "Experience Letter" => "Relieving",
        _ => throw new ArgumentException("No supplied template exists for this document type.")
    };

    public LetterTemplate Load(string company, string type)
    {
        if (!EmployeeCompany.IsValid(company)) throw new ArgumentException("Assign the employee to a company before generating a document.");
        var file = Path.Combine(contentRoot, "DocumentTemplates", "CompanyLetters", $"{company}-{TemplateKind(type)}.json");
        return JsonSerializer.Deserialize<LetterTemplate>(File.ReadAllText(file)) ?? throw new InvalidDataException("Empty letter template.");
    }

    public static Dictionary<string, string> Values(EmployeeDocumentFormViewModel m)
    {
        static string Date(DateOnly? date) => date?.ToString("dd MMMM yyyy", India) ?? "";
        var today = DateOnly.FromDateTime(DateTime.Today);
        var year = today.Month >= 4 ? today.Year : today.Year - 1;
        var values = new Dictionary<string,string>
        {
            ["CompanyName"]=m.CompanyName, ["CompanyReference"]=m.CompanyCode == "VPC" ? "Vertex Power" : "Vertex Automation",
            ["FinancialYear"]=$"{year}-{(year+1)%100:D2}", ["IssueDate"]=Date(today), ["EmployeeCode"]=m.EmployeeCode,
            ["EmployeeName"]=m.EmployeeName, ["Designation"]=m.Designation, ["Department"]=m.Department,
            ["ManagerName"]=m.ManagerName, ["EffectiveDate"]=Date(m.EffectiveDate), ["JoiningDate"]=Date(m.JoiningDate),
            ["ResignationDate"]=Date(m.ResignationDate), ["DateOfBirth"]=Date(m.DateOfBirth), ["AadhaarNumber"]=m.AadhaarNumber ?? "",
            ["FatherName"]=m.FatherName ?? "", ["PresentAddress"]=m.PresentAddress ?? "", ["PermanentAddress"]=m.PermanentAddress ?? "",
            ["PresentPinCode"]=m.PresentPinCode ?? "", ["PermanentPinCode"]=m.PermanentPinCode ?? "", ["Mobile"]=m.Mobile,
            ["AnnualCtc"]=m.AnnualCtc ?? "", ["AnnualCtcWords"]=m.AnnualCtcWords ?? "",
            ["MonthlyCtc"]=m.MonthlyCtc?.ToString("N2",India) ?? ""
        };
        var basic=m.BasicSalary??0; var hra=(m.HouseRentAllowance??0)+(m.ConveyanceAllowance??0)+(m.SpecialAllowance??0);
        var fixedPay=basic+hra; var epr=m.PerformanceReviewPay??0; var benefits=m.OtherBenefits??0;
        var employeePf=m.ProvidentFund??0; var employeeEsi=m.ProfessionalTax??0;
        var employerPf=m.EmployerProvidentFund??0; var employerEsi=m.EmployerEsi??0;
        foreach (var (key,value) in new Dictionary<string,decimal>
        {
            ["Basic"]=basic,["HraOther"]=hra,["Fixed"]=fixedPay,["EmployeePf"]=employeePf,["EmployeeEsi"]=employeeEsi,
            ["EmployeeContribution"]=employeePf+employeeEsi,["EmployerPf"]=employerPf,["EmployerEsi"]=employerEsi,
            ["EmployerContribution"]=employerPf+employerEsi,["Epr"]=epr,["OtherBenefits"]=benefits,
            ["Net"]=fixedPay+epr+benefits-employeePf-employeeEsi,["Gross"]=fixedPay+epr+benefits,["Ctc"]=fixedPay+epr+benefits+employerPf+employerEsi
        })
        { values[key+"Monthly"]=value.ToString("N2",India); values[key+"Yearly"]=(value*12).ToString("N2",India); }
        return values;
    }

    public static string Resolve(string text, IReadOnlyDictionary<string,string> values) =>
        Regex.Replace(text, @"\{\{(\w+)\}\}", match => values.TryGetValue(match.Groups[1].Value,out var value) ? value : throw new InvalidDataException("Unknown template field: "+match.Value));

    public byte[] Build(EmployeeDocumentFormViewModel model)
    {
        if(!EmployeeCompany.IsValid(model.CompanyCode)) throw new ArgumentException("Assign the employee to a company before generating a document.");
        var template=model.DocumentType=="Increment / Promotion Letter" ? IncrementTemplate(model) : Load(model.CompanyCode!,model.DocumentType);
        var values=Values(model);
        using var document=new PdfDocument();
        document.Info.Title=$"{model.CompanyName} - {model.DocumentType}";
        using var stationery=XImage.FromFile(Path.Combine(contentRoot,"DocumentTemplates","Vertex-Offer-Letter-Stationery.png"));
        using var powerHeader=XImage.FromFile(Path.Combine(contentRoot,"DocumentTemplates","CompanyLetters","Power-Header.jpeg"));
        var regular=new XFont("VertexSans",10.5); var bold=new XFont("VertexSans",10.5,XFontStyleEx.Bold);
        var title=new XFont("VertexSans",14,XFontStyleEx.Bold); var small=new XFont("VertexSans",8.2);
        var ink=new XSolidBrush(XColor.FromArgb(25,35,52));
        const double left=58,width=481,bottom=762,lineHeight=15;
        XGraphics? graphics=null;
        double y=0;
        void NewPage()
        {
            graphics?.Dispose();
            var page=document.AddPage();page.Width=XUnit.FromPoint(597);page.Height=XUnit.FromPoint(843);
            graphics=XGraphics.FromPdfPage(page);
            DrawLetterhead(graphics,stationery,powerHeader,model.CompanyCode!);
            y=130;
        }
        void Ensure(double height) { if(y+height>bottom) NewPage(); }
        List<string> Wrap(string text,XFont font,double available)
        {
            var lines=new List<string>();
            foreach(var paragraph in text.Replace("\r", "").Split('\n'))
            {
                var line="";
                foreach(var word in paragraph.Split(' ',StringSplitOptions.RemoveEmptyEntries))
                {
                    var candidate=line.Length==0?word:line+" "+word;
                    if(graphics!.MeasureString(candidate,font).Width<=available) { line=candidate;continue; }
                    if(line.Length>0) {lines.Add(line);line="";}
                    foreach(var character in word)
                    {
                        if(graphics.MeasureString(line+character,font).Width>available && line.Length>0) {lines.Add(line);line="";}
                        line+=character;
                    }
                }
                lines.Add(line);
            }
            return lines;
        }
        void Paragraph(LetterParagraph p)
        {
            var text=Resolve(p.Text,values);var font=p.Title?title:p.Bold?bold:regular;
            if(text.Trim().Equals("ANNEXURE-2",StringComparison.OrdinalIgnoreCase) && y>140) NewPage();
            var lines=Wrap(text,font,width);
            // Keep headings with the next lines; never clamp or discard overflowing text.
            Ensure(Math.Min(lines.Count*lineHeight+7+(p.Bold?30:0),bottom-130));
            foreach(var line in lines)
            {
                Ensure(lineHeight);
                graphics!.DrawString(line,font,ink,new XRect(left,y,width,lineHeight+5),p.Title?XStringFormats.TopCenter:XStringFormats.TopLeft);
                y+=p.Title?20:lineHeight;
            }
            y+=p.Title?14:7;
        }
        void Table(LetterTable table)
        {
            if(table.Kind is "Salary" or "Voucher" && y>140) NewPage();
            var widths=table.Widths.Select(w=>width*w/table.Widths.Sum()).ToArray();
            if(table.Kind=="Salary") widths=[117,54,57,10,135,54,54];
            var font=table.Kind=="Address"?regular:small;
            var rowLine=table.Kind=="Address"?15:11;
            var pen=new XPen(XColor.FromArgb(190,200,215),.5);
            void DrawRow(List<LetterCell> row)
            {
                var column=0;
                var cells=row.Select(cell=>
                {
                    var cellWidth=widths.Skip(column).Take(cell.Span).Sum();column+=cell.Span;
                    return (Width:cellWidth, Lines:Wrap(string.Join("\n",cell.Paragraphs.Select(p=>Resolve(p.Text,values))),font,cellWidth-10));
                }).ToList();
                var height=Math.Max(20,cells.Max(c=>c.Lines.Count)*rowLine+10);
                Ensure(height);
                var x=left;
                foreach(var cell in cells)
                {
                    graphics!.DrawRectangle(pen,x,y,cell.Width,height);
                    for(var i=0;i<cell.Lines.Count;i++) graphics.DrawString(cell.Lines[i],font,ink,new XRect(x+5,y+5+i*rowLine,cell.Width-10,rowLine+4),XStringFormats.TopLeft);
                    x+=cell.Width;
                }
                y+=height;
            }
            foreach(var row in table.Rows)
            {
                if(table.Kind!="Voucher" && row.All(cell=>cell.Paragraphs.Count==0)) continue;
                DrawRow(row);
            }
            y+=12;
        }
        NewPage();
        foreach(var block in template.Blocks)
        {
            if(block.Paragraph!=null) Paragraph(block.Paragraph);
            if(block.Table!=null) Table(block.Table);
        }
        if(!string.IsNullOrWhiteSpace(model.AdditionalNotes))
        { Paragraph(new(){Text="Additional Information",Bold=true});Paragraph(new(){Text=model.AdditionalNotes}); }
        graphics!.Dispose();graphics=null;
        for(var i=0;i<document.PageCount;i++)
        {
            using var footer=XGraphics.FromPdfPage(document.Pages[i],XGraphicsPdfPageOptions.Append);
            footer.DrawString($"Page {i+1} of {document.PageCount}",small,ink,new XRect(left,785,width,14),XStringFormats.TopRight);
        }
        using var output=new MemoryStream();document.Save(output,false);return output.ToArray();
    }

    public static void DrawLetterhead(XGraphics graphics,XImage stationery,XImage powerHeader,string companyCode)
    {
        graphics.DrawImage(stationery,0,0,597,843);
        if(companyCode=="VPC")
        {
            graphics.DrawRectangle(XBrushes.White,60,8,490,70);
            graphics.DrawImage(powerHeader,68,14,481,481d*powerHeader.PixelHeight/powerHeader.PixelWidth);
        }
        else
        {
            // Retain the app's logo, tagline, address and footer; correct only the company title.
            graphics.DrawRectangle(XBrushes.White,190,11,350,20);
            var font=new XFont("VertexSans",16,XFontStyleEx.Bold);
            graphics.DrawString(EmployeeCompany.DisplayName("VAS"),font,new XSolidBrush(XColor.FromArgb(35,40,85)),new XRect(192,12,340,20),XStringFormats.TopCenter);
        }
    }

    private static LetterTemplate IncrementTemplate(EmployeeDocumentFormViewModel m)
    {
        var text=new List<string> { "Date: {{IssueDate}}", "INCREMENT / PROMOTION LETTER", "To,", "{{EmployeeName}}",m.Email,m.Mobile,"Dear {{EmployeeName}},",
            "In recognition of your contribution and performance, we are pleased to confirm your increment / promotion with effect from {{EffectiveDate}}.",
            $"Change Type: {m.IncrementType}","Current Designation: {{Designation}}",$"New Designation: {m.NewDesignation}",
            $"Current Compensation: {m.CurrentCompensation}",$"Revised Compensation: {m.RevisedCompensation}",$"Increment: {m.IncrementPercentage}","Reporting Manager: {{ManagerName}}",
            "All other terms and conditions of your employment remain unchanged unless separately communicated in writing. We appreciate your continued commitment and wish you further success." };
        if(!string.IsNullOrWhiteSpace(m.PromotionReason)) text.Add(m.PromotionReason);
        text.AddRange(["For {{CompanyName}}","Authorized Signatory","Human Resources"]);
        return new(){Blocks=text.Select(t=>new LetterBlock{Paragraph=new(){Text=t,Title=t=="INCREMENT / PROMOTION LETTER"}}).ToList()};
    }
}

public sealed class LetterTemplate
{
    public string SourceFile {get;set;}="";
    public string CompanyCode {get;set;}="";
    public int SourceParagraphCount {get;set;}
    public List<LetterBlock> Blocks {get;set;}=[];
}
public sealed class LetterBlock { public LetterParagraph? Paragraph {get;set;} public LetterTable? Table {get;set;} }
public sealed class LetterParagraph
{
    public int SourceId {get;set;}
    public string SourceText {get;set;}="";
    public string Text {get;set;}="";
    public bool Bold {get;set;}
    public bool Title {get;set;}
}
public sealed class LetterTable { public string Kind {get;set;}=""; public List<double> Widths {get;set;}=[]; public List<List<LetterCell>> Rows {get;set;}=[]; }
public sealed class LetterCell { public int Span {get;set;}=1; public List<LetterParagraph> Paragraphs {get;set;}=[]; }
