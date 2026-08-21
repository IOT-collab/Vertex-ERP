using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;
using System.Text.Json;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Vertex_ERP.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin,HR")]
    public class HrController : Controller
    {
        private const long MaximumPhotoSize = 5 * 1024 * 1024;
        private readonly ApplicationDbContext _dbContext;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<HrController> _logger;
        private readonly BankAccountProtectionService _bankProtection;

        public HrController(ApplicationDbContext dbContext, IWebHostEnvironment environment, ILogger<HrController> logger, BankAccountProtectionService bankProtection)
        {
            _dbContext = dbContext;
            _environment = environment;
            _logger = logger;
            _bankProtection = bankProtection;
        }

        public IActionResult EmployeeDashboard()
        {
            return RedirectToAction("Index", "Employee");
        }

        public IActionResult AttendanceDashboard()
        {
            return RedirectToAction("Attendence", "Main");
        }

        public IActionResult EmpLeaveManagement()
        {
            return View();
        }

        public IActionResult EmpPayroll()
        {
            return View();
        }

        public IActionResult EmpPerformance()
        {
            return View();
        }

        public IActionResult Recuirement()
        {
            return View();
        }

        public async Task<IActionResult> EmpDocuments()
        {
            var documents = await _dbContext.EmployeeDocuments.AsNoTracking().Include(item => item.Employee)
                .OrderByDescending(item => item.UploadedAtUtc).Select(item => new
                {
                    item.Id, Name = item.DocumentName, Code = "DOC-" + item.Id.ToString("D5"),
                    FileSize = item.FileSize < 1024 * 1024 ? $"{item.FileSize / 1024d:F0} KB" : $"{item.FileSize / 1024d / 1024d:F1} MB",
                    Extension = Path.GetExtension(item.OriginalFileName).TrimStart('.'), EmployeeName = item.Employee.FullName,
                    EmployeeId = item.Employee.EmployeeCode, item.Employee.Department, item.Employee.Designation,
                    Category = item.DocumentType, UploadDate = item.UploadedAtUtc.ToLocalTime().ToString("dd MMM yyyy"),
                    ExpiryDate = item.ExpiryDate.HasValue ? item.ExpiryDate.Value.ToString("dd MMM yyyy") : "—",
                    Status = item.ExpiryDate.HasValue && item.ExpiryDate.Value <= DateOnly.FromDateTime(DateTime.Today.AddDays(30)) ? "Expiring Soon" : "Verified"
                }).ToListAsync();
            return View(new
            {
                Title = "Employee Documents", Documents = documents, TotalDocuments = documents.Count,
                PendingReview = 0, ExpiringSoon = documents.Count(item => item.Status == "Expiring Soon"),
                VerifiedRate = documents.Count == 0 ? "0%" : $"{documents.Count(item => item.Status == "Verified") * 100 / documents.Count}%"
            });
        }

        [HttpGet]
        public async Task<IActionResult> GenerateDocument(int? employeeId)
        {
            var model = new EmployeeDocumentFormViewModel { EmployeeId = employeeId };
            await PopulateDocumentEmployeesAsync(model);
            if (employeeId.HasValue)
            {
                var employee = await _dbContext.Employees.AsNoTracking().Include(item => item.ReportingManager).FirstOrDefaultAsync(item => item.Id == employeeId.Value);
                if (employee != null) FillDocumentEmployee(model, employee);
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateDocument(EmployeeDocumentFormViewModel model)
        {
            var allowedTypes = new[] { "Offer Letter", "Increment / Promotion Letter", "Relieving Letter", "Experience Letter" };
            if (!allowedTypes.Contains(model.DocumentType)) ModelState.AddModelError(nameof(model.DocumentType), "Select a valid document type.");
            if (model.DocumentType == "Offer Letter")
            {
                if (string.IsNullOrWhiteSpace(model.PanNumber)) ModelState.AddModelError(nameof(model.PanNumber), "PAN number is required for an offer letter.");
                if (string.IsNullOrWhiteSpace(model.AnnualCtc)) ModelState.AddModelError(nameof(model.AnnualCtc), "Annual CTC is required for an offer letter.");
                if (string.IsNullOrWhiteSpace(model.WorkLocation)) ModelState.AddModelError(nameof(model.WorkLocation), "Work location is required for an offer letter.");
                if (!model.BasicSalary.HasValue) ModelState.AddModelError(nameof(model.BasicSalary), "Basic salary is required for an offer letter.");
            }
            if (model.DocumentType == "Increment / Promotion Letter" && string.IsNullOrWhiteSpace(model.RevisedCompensation))
                ModelState.AddModelError(nameof(model.RevisedCompensation), "Revised compensation is required for an increment / promotion letter.");
            if (model.DocumentType == "Relieving Letter" && string.IsNullOrWhiteSpace(model.ClearanceStatus))
                ModelState.AddModelError(nameof(model.ClearanceStatus), "Clearance status is required for a relieving letter.");
            if (!ModelState.IsValid) { await PopulateDocumentEmployeesAsync(model); return View(model); }
            var pdf = BuildEmployeeDocumentPdf(model);
            Response.Headers.ContentDisposition = $"inline; filename=\"{BuildDocumentFileName(model)}\"";
            return File(pdf, "application/pdf");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DownloadGeneratedDocument(EmployeeDocumentFormViewModel model)
        {
            if (!ModelState.IsValid) return BadRequest("Document details are incomplete.");
            var allowedTypes = new[] { "Offer Letter", "Increment / Promotion Letter", "Relieving Letter", "Experience Letter" };
            if (!allowedTypes.Contains(model.DocumentType)) return BadRequest("Invalid document type.");
            return File(BuildEmployeeDocumentPdf(model), "application/pdf", BuildDocumentFileName(model));
        }

        private byte[] BuildEmployeeDocumentPdf(EmployeeDocumentFormViewModel model)
        {
            var templatePath = Path.Combine(_environment.ContentRootPath, "DocumentTemplates", "Vertex-Offer-Letter-Template.pdf");
            if (!System.IO.File.Exists(templatePath))
                throw new FileNotFoundException("The official HR letterhead template is missing.", templatePath);

            var stationeryPath = Path.Combine(_environment.ContentRootPath, "DocumentTemplates", "Vertex-Offer-Letter-Stationery.png");
            if (!System.IO.File.Exists(stationeryPath))
                throw new FileNotFoundException("The official HR letterhead stationery image is missing.", stationeryPath);
            using var document = new PdfDocument();
            var page = AddStationeryPage();
            if (model.DocumentType == "Offer Letter")
            {
                AddStationeryPage();
                AddStationeryPage();
            }
            using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var bodyFont = new XFont("VertexSans", 10.5, XFontStyleEx.Regular);
            var boldFont = new XFont("VertexSans", 10.5, XFontStyleEx.Bold);
            var titleFont = new XFont("VertexSans", 14, XFontStyleEx.Bold);
            var smallFont = new XFont("VertexSans", 9, XFontStyleEx.Regular);
            var ink = new XSolidBrush(XColor.FromArgb(25, 35, 52));
            const double left = 58;
            const double right = 539;
            const double lineHeight = 16;
            var y = 138d;

            graphics.DrawString($"Date: {DateTime.Today:dd MMMM yyyy}", bodyFont, ink, new XRect(left, y, right - left, 18), XStringFormats.TopRight);
            y += 34;
            graphics.DrawString(model.DocumentType.ToUpperInvariant(), titleFont, ink, new XRect(left, y, right - left, 22), XStringFormats.TopCenter);
            y += 38;

            DrawLine("To,", bodyFont);
            DrawLine(model.EmployeeName, boldFont);
            DrawLine(model.Email, bodyFont);
            DrawLine(model.Mobile, bodyFont);
            y += 10;
            DrawLine($"Dear {model.EmployeeName},", bodyFont);
            y += 8;

            var effectiveDate = model.EffectiveDate.ToString("dd MMMM yyyy");
            if (model.DocumentType == "Offer Letter")
            {
                DrawParagraph($"We are pleased to offer you the position of {model.Designation} in the {model.Department} department at Vertex Automation System (P.) Ltd., effective from {effectiveDate}.");
                DrawLabelValue("Designation", model.Designation);
                DrawLabelValue("Department", model.Department);
                DrawLabelValue("Reporting Manager", model.ManagerName);
                DrawLabelValue("Joining Date", effectiveDate);
                DrawLabelValue("PAN Number", model.PanNumber);
                DrawLabelValue("Work Location", model.WorkLocation);
                DrawLabelValue("Annual CTC", model.AnnualCtc);
                if (!string.IsNullOrWhiteSpace(model.MonthlyGross)) DrawLabelValue("Monthly Gross", model.MonthlyGross);
                if (!string.IsNullOrWhiteSpace(model.ProbationPeriod)) DrawLabelValue("Probation", model.ProbationPeriod);
                y += 8;
                DrawParagraph("This appointment is subject to the terms and conditions set out in the following pages of this letter.");
            }
            else if (model.DocumentType == "Increment / Promotion Letter")
            {
                DrawParagraph($"In recognition of your contribution and performance, we are pleased to confirm your increment / promotion with effect from {effectiveDate}.");
                if (!string.IsNullOrWhiteSpace(model.IncrementType)) DrawLabelValue("Change Type", model.IncrementType);
                DrawLabelValue("Current Designation", model.Designation);
                if (!string.IsNullOrWhiteSpace(model.NewDesignation)) DrawLabelValue("New Designation", model.NewDesignation);
                if (!string.IsNullOrWhiteSpace(model.CurrentCompensation)) DrawLabelValue("Current Compensation", model.CurrentCompensation);
                DrawLabelValue("Revised Compensation", model.RevisedCompensation);
                if (!string.IsNullOrWhiteSpace(model.IncrementPercentage)) DrawLabelValue("Increment", model.IncrementPercentage);
                DrawLabelValue("Reporting Manager", model.ManagerName);
                y += 8;
                DrawParagraph("All other terms and conditions of your employment remain unchanged unless separately communicated in writing. We appreciate your continued commitment and wish you further success.");
            }
            else if (model.DocumentType == "Relieving Letter")
            {
                DrawParagraph($"This is to confirm that you are relieved from your duties as {model.Designation} in the {model.Department} department at the close of business on {effectiveDate}.");
                if (model.ResignationDate.HasValue) DrawLabelValue("Resignation Date", model.ResignationDate.Value.ToString("dd MMMM yyyy"));
                DrawLabelValue("Clearance Status", model.ClearanceStatus);
                DrawParagraph($"During your employment you reported to {model.ManagerName}. Subject to completion of the required handover and clearance formalities, the company acknowledges the conclusion of your employment.");
                DrawParagraph("We thank you for your contribution and wish you success in your future endeavours.");
            }
            else
            {
                var joiningDate = model.JoiningDate?.ToString("dd MMMM yyyy") ?? "the recorded joining date";
                DrawParagraph($"This is to certify that {model.EmployeeName} was employed with Vertex Automation System (P.) Ltd. from {joiningDate} to {effectiveDate}.");
                DrawParagraph($"During this period, {model.EmployeeName} worked as {model.Designation} in the {model.Department} department and reported to {model.ManagerName}.");
                DrawParagraph("During the tenure with the organization, the employee carried out the assigned responsibilities with professionalism and sincerity. We found the employee's conduct and performance satisfactory.");
                DrawParagraph("We appreciate the contribution made to the organization and wish the employee success in all future professional endeavours.");
            }

            if (!string.IsNullOrWhiteSpace(model.AdditionalNotes))
            {
                y += 5;
                DrawLine("Additional Information:", boldFont);
                DrawParagraph(model.AdditionalNotes.Trim());
            }

            if (model.DocumentType == "Offer Letter")
            {
                DrawOfferSalaryAndTermsPage(document.Pages[1]);
                DrawOfferTermsPage(document.Pages[2], 3, "GENERAL TERMS AND ACCEPTANCE", new[]
                {
                    "4. Working hours and attendance: You must follow the notified working hours, attendance, leave, remote-work and overtime policies. Attendance must be recorded through the approved company system wherever applicable.",
                    "5. Place of work: Your primary work location is stated on page one. The company may require you to work at another office, customer site or project location based on business requirements."
                    ,"6. Confidentiality and intellectual property: You must protect all confidential information, customer information, technical data, business plans and trade secrets. All work product created in the course of employment will remain the property of the company, subject to applicable law.",
                    "7. Code of conduct: You are expected to maintain professional conduct, comply with company policies, avoid conflicts of interest and act respectfully with colleagues, customers and partners. Any breach may result in disciplinary action.",
                    "8. Statutory and policy compliance: You must provide accurate documents and information, including PAN and other statutory details, and promptly inform HR of any change. Your employment is subject to the company's HR, IT, information-security and workplace policies as amended from time to time.",
                    "9. Separation: Either party may end the employment in accordance with the appointment terms, notice requirements and applicable law. On separation, all company property, data and confidential material must be returned and clearance formalities completed.",
                    "10. Acceptance: By accepting this offer, you confirm that the information provided by you is accurate and that you agree to comply with these terms and the company policies."
                }, includeAcceptance: true);
            }
            else
            {
                y = Math.Min(Math.Max(y + 35, 610), 690);
                DrawLine("For Vertex Automation System (P.) Ltd.", bodyFont);
                y += 36;
                DrawLine("Authorized Signatory", boldFont);
                DrawLine("Human Resources", smallFont);
            }

            using var stream = new MemoryStream();
            document.Save(stream, false);
            return stream.ToArray();

            void DrawLine(string? text, XFont font)
            {
                graphics.DrawString(text ?? string.Empty, font, ink, new XPoint(left, y));
                y += lineHeight;
            }

            void DrawLabelValue(string label, string? value)
            {
                graphics.DrawString(label + ":", boldFont, ink, new XPoint(left + 12, y));
                graphics.DrawString(value ?? string.Empty, bodyFont, ink, new XPoint(left + 126, y));
                y += lineHeight;
            }

            void DrawParagraph(string text)
            {
                var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var line = string.Empty;
                foreach (var word in words)
                {
                    var candidate = string.IsNullOrEmpty(line) ? word : line + " " + word;
                    if (graphics.MeasureString(candidate, bodyFont).Width <= right - left)
                    {
                        line = candidate;
                        continue;
                    }
                    DrawLine(line, bodyFont);
                    line = word;
                }
                if (!string.IsNullOrEmpty(line)) DrawLine(line, bodyFont);
                y += 9;
            }

            void DrawOfferTermsPage(PdfPage continuationPage, int pageNumber, string heading, IEnumerable<string> paragraphs, bool includeAcceptance = false)
            {
                using var continuation = XGraphics.FromPdfPage(continuationPage, XGraphicsPdfPageOptions.Append);
                var continuationY = 138d;
                continuation.DrawString($"Date: {DateTime.Today:dd MMMM yyyy}", smallFont, ink, new XRect(left, continuationY, right - left, 18), XStringFormats.TopRight);
                continuationY += 34;
                continuation.DrawString(heading, titleFont, ink, new XRect(left, continuationY, right - left, 22), XStringFormats.TopCenter);
                continuationY += 42;
                foreach (var paragraph in paragraphs)
                {
                    var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var line = string.Empty;
                    foreach (var word in words)
                    {
                        var candidate = string.IsNullOrEmpty(line) ? word : line + " " + word;
                        if (continuation.MeasureString(candidate, bodyFont).Width <= right - left) { line = candidate; continue; }
                        continuation.DrawString(line, bodyFont, ink, new XPoint(left, continuationY));
                        continuationY += lineHeight;
                        line = word;
                    }
                    if (!string.IsNullOrEmpty(line)) { continuation.DrawString(line, bodyFont, ink, new XPoint(left, continuationY)); continuationY += lineHeight; }
                    continuationY += 11;
                }
                if (includeAcceptance)
                {
                    continuationY = Math.Max(continuationY + 18, 600);
                    continuation.DrawString("For Vertex Automation System (P.) Ltd.", bodyFont, ink, new XPoint(left, continuationY));
                    continuationY += 40;
                    continuation.DrawString("Authorized Signatory", boldFont, ink, new XPoint(left, continuationY));
                    continuation.DrawString("Accepted by: ______________________________", bodyFont, ink, new XPoint(left + 235, continuationY));
                    continuationY += lineHeight;
                    continuation.DrawString("Human Resources", smallFont, ink, new XPoint(left, continuationY));
                    continuation.DrawString(model.EmployeeName, smallFont, ink, new XPoint(left + 235, continuationY));
                }
                continuation.DrawString($"Page {pageNumber} of 3", smallFont, ink, new XRect(left, 770, right - left, 16), XStringFormats.TopRight);
            }

            void DrawOfferSalaryAndTermsPage(PdfPage continuationPage)
            {
                using var continuation = XGraphics.FromPdfPage(continuationPage, XGraphicsPdfPageOptions.Append);
                var pageY = 138d;
                continuation.DrawString($"Date: {DateTime.Today:dd MMMM yyyy}", smallFont, ink, new XRect(left, pageY, right - left, 18), XStringFormats.TopRight);
                pageY += 34;
                continuation.DrawString("COMPENSATION STRUCTURE", titleFont, ink, new XRect(left, pageY, right - left, 22), XStringFormats.TopCenter);
                pageY += 38;
                var earnings = new[] { ("Basic", model.BasicSalary ?? 0m), ("HRA", model.HouseRentAllowance ?? 0m), ("Conveyance Allowance", model.ConveyanceAllowance ?? 0m), ("Special Allowance", model.SpecialAllowance ?? 0m) };
                var deductions = new[] { ("Provident Fund", model.ProvidentFund ?? 0m), ("Professional Tax", model.ProfessionalTax ?? 0m), ("TDS", model.Tds ?? 0m), ("Other Deductions", model.OtherDeductions ?? 0m) };
                var totalSalary = earnings.Sum(row => row.Item2) - deductions.Sum(row => row.Item2);
                DrawSalaryTable(left, pageY, 235, "EARNINGS", earnings, null);
                DrawSalaryTable(left + 246, pageY, 235, "DEDUCTIONS", deductions, "TOTAL DEDUCTIONS");
                pageY += 118;
                var totalSalaryPen = new XPen(XColor.FromArgb(65, 86, 128), .7);
                continuation.DrawRectangle(new XSolidBrush(XColor.FromArgb(229, 236, 247)), left, pageY, right - left, 28);
                continuation.DrawRectangle(totalSalaryPen, left, pageY, right - left, 28);
                continuation.DrawString("TOTAL SALARY", boldFont, ink, new XRect(left + 10, pageY + 5, 220, 18), XStringFormats.TopLeft);
                continuation.DrawString($"INR {totalSalary:N2}", boldFont, ink, new XRect(left + 230, pageY + 5, right - left - 240, 18), XStringFormats.TopRight);
                pageY += 48;
                continuation.DrawString("Bank and statutory details", boldFont, ink, new XPoint(left, pageY));
                pageY += 17;
                continuation.DrawString($"Bank: {model.BankName ?? "Not provided"}    Account: {model.BankAccountNumber ?? "Not provided"}", smallFont, ink, new XPoint(left, pageY));
                pageY += 15;
                continuation.DrawString($"PF No.: {model.PfNumber ?? "Not provided"}    PF UAN: {model.PfUan ?? "Not provided"}", smallFont, ink, new XPoint(left, pageY));
                pageY += 34;
                continuation.DrawString("KEY EMPLOYMENT TERMS", titleFont, ink, new XRect(left, pageY, right - left, 22), XStringFormats.TopCenter);
                pageY += 36;
                DrawContinuationParagraph("1. Appointment and duties: You will perform the responsibilities assigned to your designation and any other reasonable duties assigned by the company. You will devote your full working time, attention and skill to the business of the company.");
                DrawContinuationParagraph("2. Probation: Your employment will be subject to the probation period stated on page one. The company will review performance, conduct, attendance and suitability for the role and may extend probation where required.");
                DrawContinuationParagraph("3. Compensation: Salary components shown above are subject to the applicable payroll structure, statutory deductions, tax laws and company policy. Any incentive, reimbursement or variable component is subject to eligibility.");
                continuation.DrawString("Page 2 of 3", smallFont, ink, new XRect(left, 770, right - left, 16), XStringFormats.TopRight);

                void DrawSalaryTable(double x, double yStart, double width, string heading, IEnumerable<(string Name, decimal Amount)> rows, string? totalLabel)
                {
                    const double rowHeight = 19;
                    var pen = new XPen(XColor.FromArgb(65, 86, 128), .7);
                    var includeTotal = !string.IsNullOrWhiteSpace(totalLabel);
                    continuation.DrawRectangle(pen, x, yStart, width, rowHeight * (includeTotal ? 6 : 5));
                    continuation.DrawRectangle(new XSolidBrush(XColor.FromArgb(229, 236, 247)), x, yStart, width, rowHeight);
                    continuation.DrawString(heading, boldFont, ink, new XRect(x + 6, yStart + 3, width - 12, rowHeight), XStringFormats.TopCenter);
                    var total = 0m; var rowIndex = 1;
                    foreach (var row in rows) { total += row.Amount; continuation.DrawLine(pen, x, yStart + rowHeight * rowIndex, x + width, yStart + rowHeight * rowIndex); continuation.DrawString(row.Name, smallFont, ink, new XPoint(x + 6, yStart + rowHeight * rowIndex + 13)); continuation.DrawString($"INR {row.Amount:N2}", smallFont, ink, new XRect(x + 92, yStart + rowHeight * rowIndex + 3, width - 98, rowHeight), XStringFormats.TopRight); rowIndex++; }
                    if (includeTotal)
                    {
                        continuation.DrawLine(pen, x, yStart + rowHeight * 5, x + width, yStart + rowHeight * 5);
                        continuation.DrawString(totalLabel!, boldFont, ink, new XPoint(x + 6, yStart + rowHeight * 5 + 13));
                        continuation.DrawString($"INR {total:N2}", boldFont, ink, new XRect(x + 92, yStart + rowHeight * 5 + 3, width - 98, rowHeight), XStringFormats.TopRight);
                    }
                }

                void DrawContinuationParagraph(string text)
                {
                    var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries); var line = string.Empty;
                    foreach (var word in words) { var candidate = string.IsNullOrEmpty(line) ? word : line + " " + word; if (continuation.MeasureString(candidate, bodyFont).Width <= right - left) { line = candidate; continue; } continuation.DrawString(line, bodyFont, ink, new XPoint(left, pageY)); pageY += lineHeight; line = word; }
                    if (!string.IsNullOrEmpty(line)) { continuation.DrawString(line, bodyFont, ink, new XPoint(left, pageY)); pageY += lineHeight; }
                    pageY += 9;
                }
            }

            PdfPage AddStationeryPage()
            {
                var stationeryPage = document.AddPage();
                stationeryPage.Width = XUnit.FromPoint(597);
                stationeryPage.Height = XUnit.FromPoint(843);
                using var background = XGraphics.FromPdfPage(stationeryPage);
                using var stationery = XImage.FromFile(stationeryPath);
                background.DrawImage(stationery, 0, 0, stationeryPage.Width.Point, stationeryPage.Height.Point);
                return stationeryPage;
            }
        }

        private static string BuildDocumentFileName(EmployeeDocumentFormViewModel model)
        {
            var safeName = string.Join("-", model.EmployeeName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            var safeType = model.DocumentType.Replace(" / ", "-").Replace(' ', '-');
            return $"{safeName}-{safeType}.pdf";
        }

        [HttpGet]
        public async Task<IActionResult> UploadDocument()
        {
            var model = new EmployeeDocumentUploadViewModel(); await PopulateUploadEmployeesAsync(model); return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(16 * 1024 * 1024)]
        public async Task<IActionResult> UploadDocument(EmployeeDocumentUploadViewModel model)
        {
            var allowedTypes = new[] { "Aadhaar Card", "PAN Card", "10th Certificate", "12th Certificate", "Graduation", "Post Graduation", "Employee Photo", "Previous Company Documents", "UAN Passbook", "Other" };
            if (!allowedTypes.Contains(model.DocumentType)) ModelState.AddModelError(nameof(model.DocumentType), "Select a valid document type.");
            var employee = model.EmployeeId.HasValue ? await _dbContext.Employees.AsNoTracking().FirstOrDefaultAsync(item => item.Id == model.EmployeeId.Value && item.IsActive) : null;
            if (employee == null) ModelState.AddModelError(nameof(model.EmployeeId), "Select a valid active employee.");
            if (model.File == null || model.File.Length == 0 || model.File.Length > 15 * 1024 * 1024) ModelState.AddModelError(nameof(model.File), "Choose a file up to 15 MB.");
            var extension = model.File == null ? string.Empty : Path.GetExtension(model.File.FileName).ToLowerInvariant();
            if (!new[] { ".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx" }.Contains(extension)) ModelState.AddModelError(nameof(model.File), "Allowed formats: PDF, JPG, PNG, DOC and DOCX.");
            if (!ModelState.IsValid) { await PopulateUploadEmployeesAsync(model); return View(model); }

            var storedName = $"{Guid.NewGuid():N}{extension}"; var directory = Path.Combine(_environment.ContentRootPath, "App_Data", "EmployeeDocuments"); Directory.CreateDirectory(directory);
            var targetPath = Path.Combine(directory, storedName);
            try
            {
                await using (var target = new FileStream(targetPath, FileMode.CreateNew)) await model.File!.CopyToAsync(target);
                _dbContext.EmployeeDocuments.Add(new EmployeeDocument
                {
                    EmployeeId = employee!.Id, DocumentType = model.DocumentType, DocumentName = model.DocumentName.Trim(),
                    OriginalFileName = Path.GetFileName(model.File.FileName), StoredFileName = storedName,
                    ContentType = extension switch { ".pdf" => "application/pdf", ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".doc" => "application/msword", ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document", _ => "application/octet-stream" },
                    FileSize = model.File.Length, ExpiryDate = model.ExpiryDate, Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim(),
                    UploadedBy = User.Identity?.Name ?? "HR", UploadedAtUtc = DateTime.UtcNow
                });
                await _dbContext.SaveChangesAsync();
            }
            catch
            {
                if (System.IO.File.Exists(targetPath)) System.IO.File.Delete(targetPath); throw;
            }
            TempData["DocumentMessage"] = $"{model.DocumentName} uploaded for {employee.FullName}."; return RedirectToAction(nameof(EmpDocuments));
        }

        [HttpGet]
        public async Task<IActionResult> ViewDocument(int id, bool download = false)
        {
            var document = await _dbContext.EmployeeDocuments.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id); if (document == null) return NotFound();
            var path = Path.Combine(_environment.ContentRootPath, "App_Data", "EmployeeDocuments", document.StoredFileName); if (!System.IO.File.Exists(path)) return NotFound();
            return PhysicalFile(path, document.ContentType, download ? document.OriginalFileName : null, enableRangeProcessing: true);
        }

        public IActionResult Holiday()
        {
            return View();
        }

        private async Task PopulateDocumentEmployeesAsync(EmployeeDocumentFormViewModel model)
        {
            model.Employees = await _dbContext.Employees.AsNoTracking().Include(item => item.ReportingManager)
                .Where(item => item.IsActive).OrderBy(item => item.FullName)
                .Select(item => new EmployeeDocumentEmployeeOption
                {
                    Id = item.Id, EmployeeCode = item.EmployeeCode, Name = item.FullName,
                    DateOfBirth = item.DateOfBirth.HasValue ? item.DateOfBirth.Value.ToString("yyyy-MM-dd") : null,
                    JoiningDate = item.JoiningDate.ToString("yyyy-MM-dd"),
                    Mobile = item.PhoneNumber, Email = item.Email, Designation = item.Designation,
                    Department = item.Department, ManagerName = item.ReportingManager != null ? item.ReportingManager.FullName : "Not assigned"
                }).ToListAsync();
        }

        private static void FillDocumentEmployee(EmployeeDocumentFormViewModel model, Employee employee)
        {
            model.EmployeeName = employee.FullName; model.DateOfBirth = employee.DateOfBirth; model.JoiningDate = employee.JoiningDate; model.Mobile = employee.PhoneNumber;
            model.Email = employee.Email; model.Designation = employee.Designation; model.Department = employee.Department;
            model.ManagerName = employee.ReportingManager?.FullName ?? "Not assigned";
        }

        private async Task PopulateUploadEmployeesAsync(EmployeeDocumentUploadViewModel model)
        {
            model.Employees = await _dbContext.Employees.AsNoTracking().Include(item => item.ReportingManager).Where(item => item.IsActive).OrderBy(item => item.FullName)
                .Select(item => new EmployeeDocumentEmployeeOption { Id = item.Id, EmployeeCode = item.EmployeeCode, Name = item.FullName, Designation = item.Designation, Department = item.Department, Email = item.Email, Mobile = item.PhoneNumber, ManagerName = item.ReportingManager != null ? item.ReportingManager.FullName : "Not assigned" }).ToListAsync();
        }
        public async Task<IActionResult> Department()
        {
            var departments = await _dbContext.Departments.AsNoTracking()
                .OrderBy(department => department.DepartmentName)
                .Select(department => new DepartmentOverviewItem
                {
                    Id = department.Id,
                    Name = department.DepartmentName,
                    Code = department.DepartmentCode,
                    Description = department.Description,
                    EmployeeCount = department.Employees.Count,
                    Status = department.IsActive ? "Active" : "Inactive",
                    ManagerName = department.Manager != null ? department.Manager.FullName : "Not Assigned"
                })
                .ToListAsync();

            return View(new DepartmentOverviewViewModel
            {
                Departments = departments,
                TotalDepartments = departments.Count,
                ActiveDepartments = departments.Count(department => department.Status == "Active"),
                TotalEmployees = await _dbContext.Employees.CountAsync()
            });
        }

        [HttpGet]
        public async Task<IActionResult> AddDepartment()
            => View(await PopulateDepartmentManagersAsync(new AddDepartmentViewModel()));

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddDepartment(AddDepartmentViewModel model)
        {
            var departmentName = model.DepartmentName?.Trim() ?? string.Empty;
            var departmentCode = model.DepartmentCode?.Trim() ?? string.Empty;

            if (await _dbContext.Departments.AnyAsync(department => department.DepartmentName.ToLower() == departmentName.ToLower()))
                ModelState.AddModelError(nameof(model.DepartmentName), "Department name already exists.");
            if (await _dbContext.Departments.AnyAsync(department => department.DepartmentCode.ToLower() == departmentCode.ToLower()))
                ModelState.AddModelError(nameof(model.DepartmentCode), "Department code already exists.");
            if (model.ManagerId.HasValue && !await _dbContext.Employees.AnyAsync(employee => employee.Id == model.ManagerId.Value && employee.IsActive))
                ModelState.AddModelError(nameof(model.ManagerId), "Please select a valid active employee as manager.");

            if (!ModelState.IsValid) return View(await PopulateDepartmentManagersAsync(model));

            _dbContext.Departments.Add(new Department
            {
                DepartmentName = departmentName,
                DepartmentCode = departmentCode,
                Description = Clean(model.Description),
                IsActive = model.IsActive,
                ManagerId = model.ManagerId,
                CreatedDate = DateTime.UtcNow
            });

            try
            {
                await _dbContext.SaveChangesAsync();

                TempData["DepartmentMessage"] = "Department added successfully.";
                return RedirectToAction(nameof(Department));
            }
            catch (DbUpdateException exception)
            {
                _logger.LogError(exception, "Database error while adding department {DepartmentCode}.", departmentCode);
                ModelState.AddModelError(string.Empty, "Unable to add department. The name or code may already exist.");
                return View(await PopulateDepartmentManagersAsync(model));
            }
        }

        [HttpGet]
        public async Task<IActionResult> EditDepartment(int id)
        {
            var department = await _dbContext.Departments.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id);
            if (department == null) return NotFound();
            return View("AddDepartment", await PopulateDepartmentManagersAsync(new AddDepartmentViewModel
            {
                Id = department.Id,
                DepartmentName = department.DepartmentName,
                DepartmentCode = department.DepartmentCode,
                Description = department.Description,
                IsActive = department.IsActive,
                ManagerId = department.ManagerId
            }));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditDepartment(int id, AddDepartmentViewModel model)
        {
            if (id != model.Id) return BadRequest();
            var department = await _dbContext.Departments.FirstOrDefaultAsync(item => item.Id == id);
            if (department == null) return NotFound();

            var departmentName = model.DepartmentName?.Trim() ?? string.Empty;
            var departmentCode = model.DepartmentCode?.Trim() ?? string.Empty;
            if (await _dbContext.Departments.AnyAsync(item => item.Id != id && item.DepartmentName.ToLower() == departmentName.ToLower()))
                ModelState.AddModelError(nameof(model.DepartmentName), "Department name already exists.");
            if (await _dbContext.Departments.AnyAsync(item => item.Id != id && item.DepartmentCode.ToLower() == departmentCode.ToLower()))
                ModelState.AddModelError(nameof(model.DepartmentCode), "Department code already exists.");
            if (model.ManagerId.HasValue && !await _dbContext.Employees.AnyAsync(employee => employee.Id == model.ManagerId.Value && employee.IsActive))
                ModelState.AddModelError(nameof(model.ManagerId), "Please select a valid active employee as manager.");

            if (!ModelState.IsValid) return View("AddDepartment", await PopulateDepartmentManagersAsync(model));

            department.DepartmentName = departmentName;
            department.DepartmentCode = departmentCode;
            department.Description = Clean(model.Description);
            department.IsActive = model.IsActive;
            department.ManagerId = model.ManagerId;
            department.UpdatedDate = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();
            TempData["DepartmentMessage"] = "Department updated successfully.";
            return RedirectToAction(nameof(Department));
        }

        [HttpGet]
        public async Task<IActionResult> DepartmentDetails(int id)
        {
            var department = await _dbContext.Departments.AsNoTracking()
                .Include(item => item.Manager)
                .Include(item => item.Employees)
                .FirstOrDefaultAsync(item => item.Id == id);
            return department == null ? NotFound() : View(department);
        }

        [HttpGet]
        public async Task<IActionResult> DeleteDepartment(int id)
        {
            var department = await _dbContext.Departments.AsNoTracking()
                .Include(item => item.Manager)
                .FirstOrDefaultAsync(item => item.Id == id);
            return department == null ? NotFound() : View(department);
        }

        [HttpPost, ActionName("DeleteDepartment")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteDepartmentConfirmed(int id)
        {
            var departmentFound = false;
            var executionStrategy = _dbContext.Database.CreateExecutionStrategy();

            await executionStrategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync();
                var department = await _dbContext.Departments.FirstOrDefaultAsync(item => item.Id == id);
                if (department == null) return;

                departmentFound = true;
                var assignedEmployees = await _dbContext.Employees
                    .Where(employee => employee.DepartmentId == id)
                    .ToListAsync();
                foreach (var employee in assignedEmployees)
                {
                    employee.DepartmentId = null;
                    employee.Department = "Unassigned";
                    employee.UpdatedDate = DateTime.UtcNow;
                }

                await _dbContext.SaveChangesAsync();
                _dbContext.Departments.Remove(department);
                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            });

            if (!departmentFound) return NotFound();
            TempData["DepartmentMessage"] = "Department deleted successfully.";
            return RedirectToAction(nameof(Department));
        }
        public IActionResult ExpenseClaim()
        {
            return View();
        }

        public IActionResult AssetManagement()
        {
            return View();
        }

        public IActionResult Meetings()
        {
            return View();
        }

        public IActionResult HrmReports()
        {
            return View();
        }


        [HttpGet]
        public async Task<IActionResult> HrAddEmp()
        {
            var model = new HrAddEmployeeViewModel();
            ApplyEmployeeExtraDrafts(model);
            return View(await PopulateManagersAsync(model));
        }

        [HttpGet]
        public IActionResult AddEmployeeBankDetails()
        {
            return View(ReadDraft<EmployeeBankDraft>("AddEmployeeBankDraft") ?? new EmployeeBankDraft());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddEmployeeBankDetails(EmployeeBankDraft model)
        {
            if (!ModelState.IsValid) return View(model);
            HttpContext.Session.SetString("AddEmployeeBankDraft", JsonSerializer.Serialize(model));
            TempData["EmployeeExtraMessage"] = "Bank details added to the new employee draft.";
            return RedirectToAction(nameof(HrAddEmp));
        }

        [HttpGet]
        public IActionResult AddEmployeeSalaryDetails()
        {
            return View(ReadDraft<EmployeeSalaryDraft>("AddEmployeeSalaryDraft") ?? new EmployeeSalaryDraft());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddEmployeeSalaryDetails(EmployeeSalaryDraft model)
        {
            if (!ModelState.IsValid) return View(model);
            HttpContext.Session.SetString("AddEmployeeSalaryDraft", JsonSerializer.Serialize(model));
            TempData["EmployeeExtraMessage"] = "Salary details added to the new employee draft.";
            return RedirectToAction(nameof(HrAddEmp));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> HrAddEmp(HrAddEmployeeViewModel model)
        {
            ApplyEmployeeExtraDrafts(model);
            var employeeCode = model.EmployeeId.Trim();
            var email = model.Email.Trim().ToLowerInvariant();
            var loginUsername = model.LoginUsername.Trim();
            // Passwords are exact, case-sensitive credentials. Never transform them after
            // HR has generated/entered the value shown on screen.
            var loginPassword = model.TemporaryPassword;
            var normalizedUsername = DatabaseInitializer.NormalizeUsername(loginUsername);
            var accountRole = string.Equals(model.Position, "Manager", StringComparison.OrdinalIgnoreCase)
                ? AccountRoleService.Manager
                : AccountRoleService.Employee;

            if (await _dbContext.AppUsers.AnyAsync(user => user.NormalizedUsername == normalizedUsername))
                ModelState.AddModelError(nameof(model.LoginUsername), "Login username already exists.");
            if (await _dbContext.Employees.AnyAsync(employee => employee.EmployeeCode.ToLower() == employeeCode.ToLower()))
                ModelState.AddModelError(nameof(model.EmployeeId), "Employee ID already exists.");
            if (await _dbContext.Employees.AnyAsync(employee => employee.Email == email))
                ModelState.AddModelError(nameof(model.Email), "Email address already exists.");
            if (await _dbContext.Employees.AnyAsync(employee => employee.PhoneNumber == model.Phone.Trim()))
                ModelState.AddModelError(nameof(model.Phone), "Mobile Number already exists.");
            if (model.ReportingManagerId.HasValue &&
                !await _dbContext.Employees.AnyAsync(employee => employee.Id == model.ReportingManagerId.Value && employee.IsActive))
                ModelState.AddModelError(nameof(model.ReportingManagerId), "Please select an active reporting manager.");

            var selectedDepartment = model.DepartmentId.HasValue
                ? await _dbContext.Departments.AsNoTracking().FirstOrDefaultAsync(department => department.Id == model.DepartmentId.Value && department.IsActive)
                : null;
            if (selectedDepartment == null)
                ModelState.AddModelError(nameof(model.DepartmentId), "Please select an active department.");

            var photoExtension = await ValidatePhotoAsync(model.EmployeePhoto);
            if (!ModelState.IsValid)
                return View(await PopulateManagersAsync(model));

            string? photoPath = null;
            try
            {
                if (model.EmployeePhoto != null && photoExtension != null)
                    photoPath = await SavePhotoAsync(model.EmployeePhoto, photoExtension);

                var firstName = model.FirstName.Trim();
                var lastName = Clean(model.LastName);
                var employee = new Employee
                {
                    EmployeeCode = employeeCode,
                    FirstName = firstName,
                    LastName = lastName,
                    FullName = $"{firstName} {lastName}".Trim(),
                    Email = email,
                    PhoneNumber = model.Phone.Trim(),
                    DateOfBirth = model.DateOfBirth,
                    Gender = Clean(model.Gender),
                    MaritalStatus = Clean(model.MaritalStatus),
                    EmergencyContact = model.EmergencyContact.Trim(),
                    Department = selectedDepartment!.DepartmentName,
                    DepartmentId = selectedDepartment.Id,
                    Designation = model.Designation.Trim(),
                    JoiningDate = model.JoiningDate,
                    EmploymentType = Clean(model.EmploymentType) ?? "Permanent",
                    ReportingManagerId = model.ReportingManagerId,
                    WorkLocation = Clean(model.WorkLocation),
                    Address = Clean(model.Address),
                    City = Clean(model.City),
                    State = Clean(model.State),
                    PinCode = Clean(model.PinCode),
                    PhotoPath = photoPath,
                    EmployeeStatus = "Active",
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow
                };

                _dbContext.Employees.Add(employee);
                await _dbContext.SaveChangesAsync();

                var loginAccount = new AppUser
                {
                    Username = loginUsername,
                    NormalizedUsername = normalizedUsername,
                    PasswordHash = CreateVerifiedPasswordHash(loginPassword),
                    Role = accountRole,
                    FullName = employee.FullName,
                    IsActive = true,
                    EmployeeId = employee.Id,
                    // HR-created credentials remain valid exactly as entered until HR or
                    // the employee explicitly changes them through an authorized flow.
                    MustChangePassword = false,
                    CreatedAt = DateTime.UtcNow
                };
                _dbContext.AppUsers.Add(loginAccount);
                await _dbContext.SaveChangesAsync();
                var savedHash = await _dbContext.AppUsers.AsNoTracking()
                    .Where(user => user.Id == loginAccount.Id)
                    .Select(user => user.PasswordHash)
                    .SingleAsync();
                if (!PasswordHashService.VerifyPassword(loginPassword, savedHash))
                    throw new InvalidOperationException("The employee login password could not be verified after saving.");
                if (!string.IsNullOrWhiteSpace(model.BankAccountNumber) && !string.IsNullOrWhiteSpace(model.BankName) && !string.IsNullOrWhiteSpace(model.BankAccountHolderName) && !string.IsNullOrWhiteSpace(model.BankIfscCode))
                {
                    var account = model.BankAccountNumber.Trim();
                    _dbContext.EmployeeBankDetails.Add(new EmployeeBankDetail { EmployeeId = employee.Id, AccountHolderName = model.BankAccountHolderName.Trim(), BankName = model.BankName.Trim(), ProtectedAccountNumber = _bankProtection.Protect(account), AccountLastFour = account[^4..], IfscCode = model.BankIfscCode.Trim().ToUpperInvariant(), BranchName = Clean(model.BankBranchName), AccountType = Clean(model.BankAccountType) ?? "Savings", PanNumber = Clean(model.PanNumber)?.ToUpperInvariant(), UanNumber = Clean(model.UanNumber), EsicNumber = Clean(model.EsicNumber), UpiId = Clean(model.UpiId), IsVerified = true, VerifiedAtUtc = DateTime.UtcNow });
                    await _dbContext.SaveChangesAsync();
                }
                if (model.BasicSalary > 0 || model.HouseRentAllowance > 0 || model.ConveyanceAllowance > 0 || model.SpecialAllowance > 0)
                {
                    _dbContext.EmployeeSalaryDetails.Add(new EmployeeSalaryDetail { EmployeeId = employee.Id, BasicSalary = model.BasicSalary, HouseRentAllowance = model.HouseRentAllowance, ConveyanceAllowance = model.ConveyanceAllowance, SpecialAllowance = model.SpecialAllowance, ProvidentFund = model.ProvidentFund, ProfessionalTax = model.ProfessionalTax, Tds = model.Tds, OtherDeductions = model.OtherDeductions, PfNumber = Clean(model.PfNumber), PfUan = Clean(model.PfUan), EffectiveFrom = model.SalaryEffectiveFrom, IsActive = true, UpdatedAtUtc = DateTime.UtcNow });
                    await _dbContext.SaveChangesAsync();
                }
                TempData["EmployeeMessage"] = $"{accountRole} and login account '{loginUsername}' added successfully.";
                TempData["CreatedLoginUsername"] = loginUsername;
                TempData["CreatedLoginPassword"] = loginPassword;
                TempData["CreatedLoginRole"] = accountRole;
                HttpContext.Session.Remove("AddEmployeeBankDraft");
                HttpContext.Session.Remove("AddEmployeeSalaryDraft");
                return RedirectToAction("Index", "Employee");
            }
            catch (DbUpdateException exception)
            {
                DeletePhotoIfPresent(photoPath);
                _logger.LogError(exception, "Database error while adding employee {EmployeeCode}.", employeeCode);
                ModelState.AddModelError(string.Empty, "Unable to add employee. Please try again.");
            }
            catch (IOException exception)
            {
                DeletePhotoIfPresent(photoPath);
                _logger.LogError(exception, "File error while adding employee {EmployeeCode}.", employeeCode);
                ModelState.AddModelError(string.Empty, "Unable to save the employee photo. Please try again.");
            }
            catch (Exception exception)
            {
                DeletePhotoIfPresent(photoPath);
                _logger.LogError(exception, "Unexpected error while adding employee {EmployeeCode}.", employeeCode);
                ModelState.AddModelError(string.Empty, "Unable to add employee. Please try again.");
            }

            return View(await PopulateManagersAsync(model));
        }

        private T? ReadDraft<T>(string key)
        {
            var json = HttpContext.Session.GetString(key);
            return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json);
        }

        private void ApplyEmployeeExtraDrafts(HrAddEmployeeViewModel model)
        {
            var bank = ReadDraft<EmployeeBankDraft>("AddEmployeeBankDraft");
            if (bank != null)
            {
                model.BankAccountHolderName = bank.BankAccountHolderName; model.BankName = bank.BankName; model.BankAccountNumber = bank.BankAccountNumber; model.ConfirmBankAccountNumber = bank.ConfirmBankAccountNumber; model.BankIfscCode = bank.BankIfscCode; model.BankBranchName = bank.BankBranchName; model.BankAccountType = bank.BankAccountType; model.PanNumber = bank.PanNumber; model.UanNumber = bank.UanNumber; model.EsicNumber = bank.EsicNumber; model.UpiId = bank.UpiId;
            }
            var salary = ReadDraft<EmployeeSalaryDraft>("AddEmployeeSalaryDraft");
            if (salary != null)
            {
                model.BasicSalary = salary.BasicSalary; model.HouseRentAllowance = salary.HouseRentAllowance; model.ConveyanceAllowance = salary.ConveyanceAllowance; model.SpecialAllowance = salary.SpecialAllowance; model.ProvidentFund = salary.ProvidentFund; model.ProfessionalTax = salary.ProfessionalTax; model.Tds = salary.Tds; model.OtherDeductions = salary.OtherDeductions; model.PfNumber = salary.PfNumber; model.PfUan = salary.PfUan; model.SalaryEffectiveFrom = salary.SalaryEffectiveFrom;
            }
        }

        private async Task<HrAddEmployeeViewModel> PopulateManagersAsync(HrAddEmployeeViewModel model)
        {
            model.Managers = await _dbContext.Employees.AsNoTracking()
                .Where(employee => employee.IsActive && _dbContext.AppUsers
                    .Any(user => user.EmployeeId == employee.Id && user.IsActive && user.Role == "Manager"))
                .OrderBy(employee => employee.FirstName)
                .ThenBy(employee => employee.LastName)
                .ToListAsync();
            model.Departments = await _dbContext.Departments.AsNoTracking()
                .Where(department => department.IsActive)
                .OrderBy(department => department.DepartmentName)
                .ToListAsync();
            return model;
        }

        private static string CreateVerifiedPasswordHash(string password)
        {
            var passwordHash = PasswordHashService.HashPassword(password);
            if (!PasswordHashService.VerifyPassword(password, passwordHash))
                throw new InvalidOperationException("Unable to create a valid employee login password.");
            return passwordHash;
        }

        private async Task<AddDepartmentViewModel> PopulateDepartmentManagersAsync(AddDepartmentViewModel model)
        {
            model.Managers = await _dbContext.Employees.AsNoTracking()
                .Where(employee => employee.IsActive)
                .OrderBy(employee => employee.FirstName)
                .ThenBy(employee => employee.LastName)
                .ToListAsync();
            return model;
        }

        private async Task<string?> ValidatePhotoAsync(IFormFile? photo)
        {
            if (photo == null) return null;
            if (photo.Length == 0 || photo.Length > MaximumPhotoSize)
            {
                ModelState.AddModelError(nameof(HrAddEmployeeViewModel.EmployeePhoto), "Photo must be smaller than 5 MB.");
                return null;
            }

            var header = new byte[8];
            await using var stream = photo.OpenReadStream();
            var bytesRead = await stream.ReadAsync(header.AsMemory(0, header.Length));
            var isJpeg = bytesRead >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
            var isPng = bytesRead >= 8 && header.SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

            if (isJpeg && photo.ContentType is "image/jpeg" or "image/jpg") return ".jpg";
            if (isPng && photo.ContentType == "image/png") return ".png";

            ModelState.AddModelError(nameof(HrAddEmployeeViewModel.EmployeePhoto), "Please select a valid JPG, JPEG or PNG image.");
            return null;
        }

        private async Task<string> SavePhotoAsync(IFormFile photo, string extension)
        {
            var fileName = $"{Guid.NewGuid():N}{extension}";
            var uploadDirectory = Path.Combine(_environment.WebRootPath, "uploads", "employees");
            Directory.CreateDirectory(uploadDirectory);
            var physicalPath = Path.Combine(uploadDirectory, fileName);
            try
            {
                await using var target = new FileStream(physicalPath, FileMode.CreateNew);
                await photo.CopyToAsync(target);
                return $"/uploads/employees/{fileName}";
            }
            catch
            {
                if (System.IO.File.Exists(physicalPath)) System.IO.File.Delete(physicalPath);
                throw;
            }
        }

        private void DeletePhotoIfPresent(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return;
            var physicalPath = Path.Combine(_environment.WebRootPath, relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(physicalPath)) System.IO.File.Delete(physicalPath);
        }

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();


    }
}
