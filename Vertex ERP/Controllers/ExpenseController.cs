using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

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
        var currentEmployeeId = await LoggedInEmployeeIdAsync();
        var viewer = currentEmployeeId.HasValue ? await db.Employees.AsNoTracking().Include(x => x.ReportingManager)
            .FirstOrDefaultAsync(x => x.Id == currentEmployeeId && x.IsActive) : null;
        var canReceive = viewer?.CanReceiveExpenseClaims == true;
        if (string.Equals(view, "inbox", StringComparison.OrdinalIgnoreCase) && !canReceive) return Forbid();
        var query = db.ExpenseClaims.AsNoTracking().Include(x => x.Employee).Include(x => x.ReportingManager)
            .Include(x => x.DecidedByUser).Include(x => x.ForwardedToEmployee).AsQueryable();
        string mode;
        if (string.Equals(view, "inbox", StringComparison.OrdinalIgnoreCase) && canReceive)
        {
            query = query.Where(x => (x.WorkflowStage == ExpenseClaimWorkflow.Forwarded || x.WorkflowStage == ExpenseClaimWorkflow.Completed) && x.ForwardedToEmployeeId == currentEmployeeId);
            mode = "Recipient";
        }
        else if (string.Equals(view, "self", StringComparison.OrdinalIgnoreCase) || (!User.IsInRole("Manager") && !User.IsInRole("HR") && !User.IsInRole("Admin")))
        {
            if (viewer == null) return Forbid();
            query = query.Where(x => x.EmployeeId == viewer.Id);
            mode = "Self";
        }
        else if (User.IsInRole("Manager"))
        {
            if (viewer == null) return Forbid();
            query = query.Where(x => x.ReportingManagerId == viewer.Id && x.EmployeeId != viewer.Id);
            mode = "Manager";
        }
        else
        {
            query = query.Where(x => x.WorkflowStage != ExpenseClaimWorkflow.Manager);
            mode = "HR";
        }
        var advances = db.ExpenseAdvances.AsNoTracking().Include(x => x.Employee).AsQueryable();
        if (mode != "HR") advances = advances.Where(x => x.EmployeeId == currentEmployeeId);
        return View("~/Views/Hr/ExpenseClaim.cshtml", new ExpenseClaimPageViewModel
        {
            Mode = mode, CurrentEmployeeId = currentEmployeeId, CanReceiveForwardedClaims = canReceive,
            HasReportingManager = viewer?.ReportingManagerId.HasValue == true, ReportingManagerName = viewer?.ReportingManager?.FullName,
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
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> Submit(SubmitExpenseClaimViewModel model)
    {
        var employeeId = await LoggedInEmployeeIdAsync();
        var employee = employeeId.HasValue ? await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == employeeId && x.IsActive) : null;
        if (employee == null) return Forbid();
        if (!employee.ReportingManagerId.HasValue || employee.ReportingManagerId == employee.Id
            || !await db.Employees.AnyAsync(x => x.Id == employee.ReportingManagerId && x.IsActive
                && db.AppUsers.Any(account => account.EmployeeId == x.Id && account.IsActive)))
            ModelState.AddModelError("", "Ask HR to assign an active reporting manager before submitting an expense.");
        ValidateReceipt(model.ReceiptFile);
        if (model.ExpenseDate == null || model.ExpenseDate > DateOnly.FromDateTime(FieldAttendanceClock.Now))
            ModelState.AddModelError(nameof(model.ExpenseDate), "Select a valid expense date, not in the future.");
        if (model.Amount != decimal.Round(model.Amount, 2)) ModelState.AddModelError(nameof(model.Amount), "Use at most two decimal places.");
        if (model.ReceiptFile != null && Path.GetFileName(model.ReceiptFile.FileName).Length > 260)
            ModelState.AddModelError(nameof(model.ReceiptFile), "Receipt filename is too long.");
        if (ModelState.IsValid && !await HasReceiptSignatureAsync(model.ReceiptFile!))
            ModelState.AddModelError(nameof(model.ReceiptFile), "The bill content must match its PDF, JPG or PNG file type.");
        if (!ModelState.IsValid)
        {
            TempData["ExpenseError"] = string.Join(" ", ModelState.Values.SelectMany(x => x.Errors).Select(x => x.ErrorMessage));
            return RedirectToAction(nameof(Index), new { view = "self" });
        }
        var file = model.ReceiptFile!;
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var storedName = $"{Guid.NewGuid():N}{extension}";
        var folder = Path.Combine(environment.ContentRootPath, "App_Data", "ExpenseReceipts");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, storedName);
        try
        {
            await using (var target = new FileStream(path, FileMode.CreateNew)) await file.CopyToAsync(target);
            db.ExpenseClaims.Add(new ExpenseClaim {
                EmployeeId = employee.Id, ReportingManagerId = employee.ReportingManagerId,
                WorkflowStage = ExpenseClaimWorkflow.Manager, RequiresHrApproval = false,
                Category = model.Category.Trim(), Title = model.Title.Trim(), ExpenseDate = model.ExpenseDate!.Value,
                Amount = model.Amount, Remarks = model.Remarks?.Trim(), StoredFileName = storedName,
                OriginalFileName = Path.GetFileName(file.FileName), ContentType = AllowedFiles[extension], FileSize = file.Length
            });
            await db.SaveChangesAsync();
        }
        catch { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); throw; }
        TempData["ExpenseMessage"] = "Expense submitted to your reporting manager. You can track each review below.";
        return RedirectToAction(nameof(Index), new { view = "self" });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Manager,HR,Admin")]
    public async Task<IActionResult> Decide(int id, string decision, string? note)
    {
        if (!ExpenseClaimWorkflow.ValidDecision(decision) || note?.Length > 500) return BadRequest();
        if (decision == "Rejected" && string.IsNullOrWhiteSpace(note)) {
            TempData["ExpenseError"] = "Please enter a reason for rejection."; return RedirectToAction(nameof(Index));
        }
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return Forbid();
        var viewerId = await LoggedInEmployeeIdAsync();
        var claim = await db.ExpenseClaims.SingleOrDefaultAsync(x => x.Id == id);
        if (claim == null) return NotFound();
        if (User.IsInRole("HR") || User.IsInRole("Admin"))
        {
            if (!ExpenseClaimWorkflow.CanHrReview(claim, viewerId)) return Forbid();
            ExpenseClaimWorkflow.HrReview(claim, decision, note?.Trim(), actorId);
        }
        else
        {
            if (!ExpenseClaimWorkflow.CanManagerReview(claim, viewerId)) return Forbid();
            ExpenseClaimWorkflow.ManagerReview(claim, decision, note?.Trim(), actorId);
        }
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { TempData["ExpenseError"] = "This claim was already updated. Refresh to see its current stage."; return RedirectToAction(nameof(Index)); }
        TempData["ExpenseMessage"] = claim.WorkflowStage == ExpenseClaimWorkflow.HR
            ? "Manager decision recorded and claim sent to HR." : "HR decision recorded. You can now forward this claim to Yuvraj.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "HR,Admin")]
    public async Task<IActionResult> Forward(int id)
    {
        var claim = await db.ExpenseClaims.SingleOrDefaultAsync(x => x.Id == id);
        if (claim == null) return NotFound();
        if (!ExpenseClaimWorkflow.CanForward(claim, await LoggedInEmployeeIdAsync())) return Forbid();
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return Forbid();
        var recipients = await db.Employees.AsNoTracking().Where(x => x.CanReceiveExpenseClaims && x.IsActive
            && db.AppUsers.Any(account => account.EmployeeId == x.Id && account.IsActive)).Take(2).ToListAsync();
        if (recipients.Count != 1) {
            TempData["ExpenseError"] = "Yuvraj's active linked expense inbox is not configured. Check his employee login.";
            return RedirectToAction(nameof(Index));
        }
        claim.ForwardedToEmployeeId = recipients[0].Id; claim.ForwardedByUserId = actorId;
        claim.ForwardedAtUtc = DateTime.UtcNow; claim.WorkflowStage = ExpenseClaimWorkflow.Forwarded;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { TempData["ExpenseError"] = "Claim already updated. Refresh to check its forwarding status."; return RedirectToAction(nameof(Index)); }
        TempData["ExpenseMessage"] = "Claim and bill forwarded to Yuvraj's expense inbox.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> Respond(RespondToExpenseClaimViewModel model)
    {
        var viewerId = await LoggedInEmployeeIdAsync();
        var hasInbox = viewerId.HasValue && await db.Employees.AnyAsync(x => x.Id == viewerId && x.IsActive && x.CanReceiveExpenseClaims);
        var claim = await db.ExpenseClaims.SingleOrDefaultAsync(x => x.Id == model.Id);
        if (claim == null) return NotFound();
        if (!ExpenseClaimWorkflow.CanRecipientRespond(claim, viewerId, hasInbox)) return Forbid();
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return Forbid();
        if (!ExpenseClaimWorkflow.ValidDecision(model.Decision)) ModelState.AddModelError(nameof(model.Decision), "Choose Approve or Reject.");
        if (string.IsNullOrWhiteSpace(model.Reason) || model.Reason.Length > 1000)
            ModelState.AddModelError(nameof(model.Reason), "Enter a reason of up to 1000 characters.");
        ValidateReceipt(model.ResponseFile, nameof(model.ResponseFile));
        if (model.ResponseFile != null && Path.GetFileName(model.ResponseFile.FileName).Length > 260)
            ModelState.AddModelError(nameof(model.ResponseFile), "Response filename is too long.");
        if (ModelState.IsValid && !await HasReceiptSignatureAsync(model.ResponseFile!))
            ModelState.AddModelError(nameof(model.ResponseFile), "Response file content must match its PDF, JPG or PNG type.");
        if (!ModelState.IsValid)
        {
            TempData["ExpenseError"] = string.Join(" ", ModelState.Values.SelectMany(x => x.Errors).Select(x => x.ErrorMessage));
            return RedirectToAction(nameof(Index), new { view = "inbox" });
        }
        var file = model.ResponseFile!;
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var storedName = $"{Guid.NewGuid():N}{extension}";
        var folder = Path.Combine(environment.ContentRootPath, "App_Data", "ExpenseResponses");
        var path = Path.Combine(folder, storedName);
        try
        {
            Directory.CreateDirectory(folder);
            await using (var target = new FileStream(path, FileMode.CreateNew)) await file.CopyToAsync(target);
            claim.ResponseStoredFileName = storedName; claim.ResponseOriginalFileName = Path.GetFileName(file.FileName);
            claim.ResponseContentType = AllowedFiles[extension]; claim.ResponseFileSize = file.Length;
            ExpenseClaimWorkflow.RecipientRespond(claim, model.Decision, model.Reason.Trim(), actorId);
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            TempData["ExpenseError"] = "This claim already has a response or was updated. Refresh to check its final status.";
            return RedirectToAction(nameof(Index), new { view = "inbox" });
        }
        catch { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); throw; }
        TempData["ExpenseMessage"] = "Final response sent. The employee can view your decision, reason and attachment in My Claims.";
        return RedirectToAction(nameof(Index), new { view = "inbox" });
    }

    [HttpGet]
    public async Task<IActionResult> ResponseAttachment(int id, bool download = false)
    {
        var claim = await db.ExpenseClaims.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (claim == null || claim.WorkflowStage != ExpenseClaimWorkflow.Completed || claim.ResponseStoredFileName == null) return NotFound();
        if (!await CanReadClaimAsync(claim)) return Forbid();
        if (Path.GetFileName(claim.ResponseStoredFileName) != claim.ResponseStoredFileName) return NotFound();
        var path = Path.Combine(environment.ContentRootPath, "App_Data", "ExpenseResponses", claim.ResponseStoredFileName);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (!System.IO.File.Exists(path)) return NotFound();
        return download ? PhysicalFile(path, claim.ResponseContentType ?? "application/octet-stream", claim.ResponseOriginalFileName)
            : PhysicalFile(path, claim.ResponseContentType ?? "application/octet-stream", enableRangeProcessing: true);
    }

    private async Task<bool> CanReadClaimAsync(ExpenseClaim claim)
    {
        var privileged = User.IsInRole("HR") || User.IsInRole("Admin");
        if (privileged) return true;
        var employeeId = await LoggedInEmployeeIdAsync();
        var hasInbox = employeeId.HasValue && claim.ForwardedToEmployeeId == employeeId
            && await db.Employees.AnyAsync(x => x.Id == employeeId && x.IsActive && x.CanReceiveExpenseClaims);
        return ExpenseClaimWorkflow.CanReadClaim(claim, employeeId, false, hasInbox);
    }

    [HttpGet]
    public async Task<IActionResult> Receipt(int id)
    {
        var claim = await db.ExpenseClaims.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (claim == null) return NotFound();
        if (!await CanReadClaimAsync(claim)) return Forbid();
        if (Path.GetFileName(claim.StoredFileName) != claim.StoredFileName) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var path = Path.Combine(environment.ContentRootPath, "App_Data", "ExpenseReceipts", claim.StoredFileName);
        return System.IO.File.Exists(path) ? PhysicalFile(path, claim.ContentType, enableRangeProcessing: true) : NotFound();
    }

    private async Task<int?> LoggedInEmployeeIdAsync()
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return null;
        return await db.AppUsers.AsNoTracking().Where(x => x.Id == userId && x.IsActive).Select(x => x.EmployeeId).FirstOrDefaultAsync();
    }

    private void ValidateReceipt(IFormFile? file, string fieldName = nameof(SubmitExpenseClaimViewModel.ReceiptFile))
    {
        if (file == null || file.Length == 0) { ModelState.AddModelError(fieldName, "Upload a bill or receipt."); return; }
        if (file.Length > MaximumReceiptSize) ModelState.AddModelError(fieldName, "Receipt must be 10 MB or smaller.");
        var extension = Path.GetExtension(file.FileName);
        if (!AllowedFiles.TryGetValue(extension, out var contentType) || !string.Equals(file.ContentType, contentType, StringComparison.OrdinalIgnoreCase))
            ModelState.AddModelError(fieldName, "Only PDF, JPG and PNG receipts are allowed.");
    }
}
