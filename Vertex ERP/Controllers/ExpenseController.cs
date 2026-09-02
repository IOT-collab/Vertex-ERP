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
        return View("~/Views/Hr/ExpenseClaim.cshtml", new ExpenseClaimPageViewModel
        { Mode = mode, Claims = await query.OrderByDescending(x => x.SubmittedAtUtc).ToListAsync() });
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
