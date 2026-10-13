using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;

namespace Vertex_ERP.Controllers;

[Authorize(Roles = "Employee,User,Manager,HR,Admin")]
public sealed class ExpenseController(ApplicationDbContext db, IWebHostEnvironment environment) : Controller
{
    private const long MaximumReceiptSize = 10 * 1024 * 1024;
    private static readonly Dictionary<string, string> AllowedFiles = new(StringComparer.OrdinalIgnoreCase)
    { [".pdf"] = "application/pdf", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png" };

    [HttpGet]
    public async Task<IActionResult> Index(string? view)
    {
        var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        var query = db.ExpenseClaims.AsNoTracking().Include(x => x.Employee).Include(x => x.ReportingManager).Include(x => x.DecidedByUser).AsQueryable();
        string mode;
        if (User.IsInRole("Manager"))
        {
            var managerId = await LoggedInEmployeeIdAsync();
            if (!managerId.HasValue) return Forbid();
            if (string.Equals(view, "self", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(x => x.EmployeeId == managerId.Value);
                mode = "Self";
            }
            else
            {
                query = query.Where(x => !x.RequiresHrApproval && x.ReportingManagerId == managerId.Value && x.EmployeeId != managerId.Value);
                mode = "Manager";
            }
        }
        else if (User.IsInRole("HR") || User.IsInRole("Admin")) mode = "HR";
        else
        {
            var employeeId = await LoggedInEmployeeIdAsync();
            if (!employeeId.HasValue) return Forbid();
            query = query.Where(x => x.EmployeeId == employeeId.Value);
            mode = "Employee";
        }
        var currentEmployeeId = await LoggedInEmployeeIdAsync();
        var advances = db.ExpenseAdvances.AsNoTracking().Include(x => x.Employee).AsQueryable();
        if (mode != "HR") advances = advances.Where(x => x.EmployeeId == currentEmployeeId);
        return View("~/Views/Hr/ExpenseClaim.cshtml", new ExpenseClaimPageViewModel
        {
            Mode = mode, CurrentEmployeeId = currentEmployeeId,
            Claims = await query.OrderByDescending(x => x.SubmittedAtUtc).ToListAsync(),
            Advances = await advances.OrderByDescending(x => x.PaidAtUtc).ToListAsync(),
            AdvanceEmployees = mode == "HR" ? await db.Employees.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.FullName).ToListAsync() : new List<Employee>()
        });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "HR,Admin")]
    public async Task<IActionResult> RecordAdvance(RecordExpenseAdvanceViewModel model)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Forbid();
        if (!await db.Employees.AnyAsync(x => x.Id == model.EmployeeId && x.IsActive))
            ModelState.AddModelError(nameof(model.EmployeeId), "Select an active employee.");
        if (!new[] { "Bank Transfer", "UPI", "Cash", "Cheque" }.Contains(model.PaymentMode))
            ModelState.AddModelError(nameof(model.PaymentMode), "Select a valid payment mode.");
        var paidAtUtc = model.PaidAtIst.HasValue && model.PaidAtIst.Value.Year >= 2000
            ? DateTime.SpecifyKind(model.PaidAtIst.Value, DateTimeKind.Unspecified).AddMinutes(-330) : (DateTime?)null;
        if (!paidAtUtc.HasValue || paidAtUtc.Value > DateTime.UtcNow)
            ModelState.AddModelError(nameof(model.PaidAtIst), "Enter a valid payment date/time in IST, not in the future.");
        if (model.Amount != decimal.Round(model.Amount, 2)) ModelState.AddModelError(nameof(model.Amount), "Use at most two decimal places.");
        ValidateReceipt(model.ReceiptFile);
        if (model.ReceiptFile != null && Path.GetFileName(model.ReceiptFile.FileName).Length > 260)
            ModelState.AddModelError(nameof(model.ReceiptFile), "Receipt filename must be 260 characters or fewer.");
        if (ModelState.IsValid && !await HasReceiptSignatureAsync(model.ReceiptFile!))
            ModelState.AddModelError(nameof(model.ReceiptFile), "The receipt content must match its PDF, JPG or PNG file type.");
        if (!ModelState.IsValid)
        {
            TempData["ExpenseError"] = string.Join(" ", ModelState.Values.SelectMany(x => x.Errors).Select(x => x.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }
        var file = model.ReceiptFile!;
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var storedName = $"{Guid.NewGuid():N}{extension}";
        var folder = Path.Combine(environment.ContentRootPath, "App_Data", "AdvanceReceipts");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, storedName);
        try
        {
            await using (var target = new FileStream(path, FileMode.CreateNew)) await file.CopyToAsync(target);
            db.ExpenseAdvances.Add(new ExpenseAdvance
            {
                EmployeeId = model.EmployeeId, Purpose = model.Purpose.Trim(), Amount = model.Amount,
                PaymentMode = model.PaymentMode, TransactionId = model.TransactionId.Trim(),
                PaidAtUtc = DateTime.SpecifyKind(paidAtUtc!.Value, DateTimeKind.Utc), RecordedByUserId = userId,
                StoredFileName = storedName, OriginalFileName = Path.GetFileName(file.FileName), ContentType = AllowedFiles[extension]
            });
            await db.SaveChangesAsync();
        }
        catch
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            throw;
        }
        TempData["ExpenseMessage"] = "Advance payment recorded. Employee confirmation is pending.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmAdvance(ConfirmExpenseAdvanceViewModel model)
    {
        var employeeId = await LoggedInEmployeeIdAsync();
        if (!employeeId.HasValue) return Forbid();
        var advance = await db.ExpenseAdvances.FirstOrDefaultAsync(x => x.Id == model.Id && x.EmployeeId == employeeId.Value);
        if (advance == null) return NotFound();
        if (advance.ConfirmationStatus == "Received") return Conflict("Payment receipt has already been confirmed.");
        if (!new[] { "Received", "Amount Mismatch", "Not Received" }.Contains(model.Response)) return BadRequest();
        if (model.Response == "Amount Mismatch" && (!model.ReceivedAmount.HasValue || model.ReceivedAmount == advance.Amount))
            ModelState.AddModelError(nameof(model.ReceivedAmount), "Enter the actual amount received, different from the payment amount.");
        if (model.ReceivedAmount.HasValue && model.ReceivedAmount != decimal.Round(model.ReceivedAmount.Value, 2))
            ModelState.AddModelError(nameof(model.ReceivedAmount), "Use at most two decimal places.");
        if (model.Response != "Received" && string.IsNullOrWhiteSpace(model.Note))
            ModelState.AddModelError(nameof(model.Note), "Please describe the payment issue.");
        if (!ModelState.IsValid)
        {
            TempData["ExpenseError"] = string.Join(" ", ModelState.Values.SelectMany(x => x.Errors).Select(x => x.ErrorMessage));
            return RedirectToAction(nameof(Index), User.IsInRole("Manager") ? new { view = "self" } : null);
        }
        advance.ConfirmationStatus = model.Response;
        advance.ReceivedAmount = model.Response == "Received" ? advance.Amount : model.Response == "Not Received" ? 0 : model.ReceivedAmount;
        advance.EmployeeNote = model.Note?.Trim();
        advance.RespondedAtUtc = DateTime.UtcNow;
        advance.RespondedByUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        db.AuditLogs.Add(VertexERP.Services.AuditLogFactory.CreateEvent("ExpenseAdvance", "Employee payment response",
            $"Advance #{advance.Id} · Employee #{advance.EmployeeId} · {advance.ConfirmationStatus} · Received {advance.ReceivedAmount:0.00}", User));
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return Conflict("This payment was updated. Refresh and check its current status."); }
        TempData["ExpenseMessage"] = "Your payment response has been shared with HR. Receipt confirmation does not settle the advance against expenses.";
        return RedirectToAction(nameof(Index), User.IsInRole("Manager") ? new { view = "self" } : null);
    }

    [HttpGet]
    public async Task<IActionResult> AdvanceReceipt(int id)
    {
        var advance = await db.ExpenseAdvances.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (advance == null) return NotFound();
        if (!User.IsInRole("HR") && !User.IsInRole("Admin") && await LoggedInEmployeeIdAsync() != advance.EmployeeId) return Forbid();
        var path = Path.Combine(environment.ContentRootPath, "App_Data", "AdvanceReceipts", advance.StoredFileName);
        return System.IO.File.Exists(path) ? PhysicalFile(path, advance.ContentType, advance.OriginalFileName) : NotFound();
    }

    private static async Task<bool> HasReceiptSignatureAsync(IFormFile file)
    {
        await using var stream = file.OpenReadStream();
        var header = new byte[8];
        var count = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false);
        return Path.GetExtension(file.FileName).ToLowerInvariant() switch
        {
            ".pdf" => count >= 5 && header.AsSpan(0, 5).SequenceEqual("%PDF-"u8),
            ".jpg" or ".jpeg" => count >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255,
            ".png" => count == 8 && header.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            _ => false
        };
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Employee,User,Manager")]
    public async Task<IActionResult> Submit(SubmitExpenseClaimViewModel model)
    {
        var employeeId = await LoggedInEmployeeIdAsync();
        var employee = employeeId.HasValue ? await db.Employees.FirstOrDefaultAsync(x => x.Id == employeeId.Value && x.IsActive) : null;
        if (employee == null) return Forbid();
        var isManagerClaim = User.IsInRole("Manager");
        if (!isManagerClaim && !employee.ReportingManagerId.HasValue)
            ModelState.AddModelError(string.Empty, "A reporting manager must be assigned before you can submit an expense claim. Please contact HR.");
        ValidateReceipt(model.ReceiptFile);
        if (model.ExpenseDate > DateOnly.FromDateTime(DateTime.Today)) ModelState.AddModelError(nameof(model.ExpenseDate), "Expense date cannot be in the future.");
        if (!ModelState.IsValid)
        {
            TempData["ExpenseError"] = string.Join(" ", ModelState.Values.SelectMany(x => x.Errors).Select(x => x.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }
        var extension = Path.GetExtension(model.ReceiptFile!.FileName).ToLowerInvariant();
        var storedName = $"{Guid.NewGuid():N}{extension}";
        var folder = Path.Combine(environment.ContentRootPath, "App_Data", "ExpenseReceipts");
        Directory.CreateDirectory(folder);
        await using (var target = new FileStream(Path.Combine(folder, storedName), FileMode.CreateNew)) await model.ReceiptFile.CopyToAsync(target);
        db.ExpenseClaims.Add(new ExpenseClaim
        {
            EmployeeId = employee.Id, ReportingManagerId = isManagerClaim ? null : employee.ReportingManagerId,
            RequiresHrApproval = isManagerClaim,
            Category = model.Category.Trim(), Title = model.Title.Trim(), ExpenseDate = model.ExpenseDate!.Value,
            Amount = model.Amount, Remarks = model.Remarks?.Trim(), StoredFileName = storedName,
            OriginalFileName = Path.GetFileName(model.ReceiptFile.FileName), ContentType = AllowedFiles[extension], FileSize = model.ReceiptFile.Length
        });
        await db.SaveChangesAsync();
        TempData["ExpenseMessage"] = isManagerClaim ? "Expense claim submitted to HR." : "Expense claim submitted to your manager.";
        return RedirectToAction(nameof(Index), isManagerClaim ? new { view = "self" } : null);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Manager,HR,Admin")]
    public async Task<IActionResult> Decide(int id, string decision, string? note)
    {
        if (!decision.Equals("Approved", StringComparison.OrdinalIgnoreCase) && !decision.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
            return BadRequest();
        var isHrReviewer = User.IsInRole("HR") || User.IsInRole("Admin");
        var managerId = isHrReviewer ? null : await LoggedInEmployeeIdAsync();
        var claim = await db.ExpenseClaims.FirstOrDefaultAsync(x => x.Id == id && x.Status == "Pending" && (isHrReviewer || (!x.RequiresHrApproval && x.ReportingManagerId == managerId)));
        if (claim == null) return NotFound();
        claim.Status = decision.Equals("Approved", StringComparison.OrdinalIgnoreCase) ? "Approved" : "Rejected";
        claim.DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 500)];
        claim.DecidedAtUtc = DateTime.UtcNow;
        claim.DecidedByUserId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
        await db.SaveChangesAsync();
        TempData["ExpenseMessage"] = $"Expense claim {claim.Status.ToLowerInvariant()}. The employee can now see this decision.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Receipt(int id)
    {
        var claim = await db.ExpenseClaims.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (claim == null) return NotFound();
        if (!User.IsInRole("HR") && !User.IsInRole("Admin"))
        {
            var employeeId = await LoggedInEmployeeIdAsync();
            if (employeeId != claim.EmployeeId && employeeId != claim.ReportingManagerId) return Forbid();
        }
        var path = Path.Combine(environment.ContentRootPath, "App_Data", "ExpenseReceipts", claim.StoredFileName);
        return System.IO.File.Exists(path) ? PhysicalFile(path, claim.ContentType, enableRangeProcessing: true) : NotFound();
    }

    private async Task<int?> LoggedInEmployeeIdAsync()
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return null;
        return await db.AppUsers.AsNoTracking().Where(x => x.Id == userId && x.IsActive).Select(x => x.EmployeeId).FirstOrDefaultAsync();
    }

    private void ValidateReceipt(IFormFile? file)
    {
        if (file == null || file.Length == 0) { ModelState.AddModelError(nameof(SubmitExpenseClaimViewModel.ReceiptFile), "Upload a bill or receipt."); return; }
        if (file.Length > MaximumReceiptSize) ModelState.AddModelError(nameof(SubmitExpenseClaimViewModel.ReceiptFile), "Receipt must be 10 MB or smaller.");
        var extension = Path.GetExtension(file.FileName);
        if (!AllowedFiles.TryGetValue(extension, out var contentType) || !string.Equals(file.ContentType, contentType, StringComparison.OrdinalIgnoreCase))
            ModelState.AddModelError(nameof(SubmitExpenseClaimViewModel.ReceiptFile), "Only PDF, JPG and PNG receipts are allowed.");
    }
}
