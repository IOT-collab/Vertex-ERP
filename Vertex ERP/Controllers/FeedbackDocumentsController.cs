using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

namespace Vertex_ERP.Controllers;

[Authorize(Roles = "Employee,User,Manager,HR,Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FeedbackDocumentsController(ApplicationDbContext db, IWebHostEnvironment environment) : Controller
{
    private bool Management => User.IsInRole("HR") || User.IsInRole("Admin");
    private async Task<int?> EmployeeIdAsync(CancellationToken token) => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? await db.AppUsers.AsNoTracking().Where(x => x.Id == id && x.IsActive && x.Employee != null && x.Employee.IsActive)
            .Select(x => x.EmployeeId).SingleOrDefaultAsync(token) : null;

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default, int? documentId = null)
    {
        var employeeId = await EmployeeIdAsync(cancellationToken);
        if (!Management && !employeeId.HasValue) return Forbid();
        var documents = await db.FeedbackDocuments.AsNoTracking().Include(x => x.Assets.Where(asset => asset.SubmissionId == null))
            .OrderByDescending(x => x.PublishedAtUtc).ThenByDescending(x => x.Id).ToListAsync(cancellationToken);
        var own = employeeId.HasValue ? await db.FeedbackDocumentSubmissions.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId).ToDictionaryAsync(x => x.DocumentId, cancellationToken) : new Dictionary<int,FeedbackDocumentSubmission>();
        var activeEmployees = Management ? await db.Employees.CountAsync(x => x.IsActive, cancellationToken) : 0;
        var counts = await db.FeedbackDocumentSubmissions.AsNoTracking().Where(x => Management)
            .GroupBy(x => x.DocumentId).Select(group => new { DocumentId = group.Key, Count = group.Count(), Inactive = group.Count(x => !x.Employee.IsActive) })
            .ToDictionaryAsync(x => x.DocumentId, cancellationToken);
        return View(new FeedbackDocumentsViewModel { IsManagement = Management,
            TotalEmployees = activeEmployees, SelectedDocumentId = documentId,
            Documents = documents.Select(doc => new FeedbackDocumentCard { Document = doc, OwnSubmission = own.GetValueOrDefault(doc.Id),
                SubmittedCount = counts.GetValueOrDefault(doc.Id)?.Count ?? 0,
                ReachedCount = activeEmployees + (counts.GetValueOrDefault(doc.Id)?.Inactive ?? 0) }).ToList() });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "HR,Admin"), RequestSizeLimit(27 * 1024 * 1024)]
    public async Task<IActionResult> Publish(PublishFeedbackDocumentForm form, CancellationToken cancellationToken)
    {
        if (!Management || !int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return Forbid();
        var error = await FeedbackDocumentStorage.ValidateAsync(form.Files);
        if (error != null) ModelState.AddModelError(nameof(form.Files), error);
        if (!ModelState.IsValid) return Failure(nameof(Index));
        var storage = new FeedbackDocumentStorage(environment);
        List<FeedbackDocumentAsset> assets = [];
        try
        {
            assets = await storage.StoreAsync(form.Files, cancellationToken);
            var document = new FeedbackDocument { Title = form.Title.Trim(), Instructions = form.Instructions?.Trim(), PublishedByUserId = actorId };
            foreach (var asset in assets) document.Assets.Add(asset);
            db.FeedbackDocuments.Add(document);
            await db.SaveChangesAsync(cancellationToken);
            TempData["FeedbackDocumentMessage"] = "Document published. All active employees can download it and upload their response.";
            return RedirectToAction(nameof(Details), new { id = document.Id });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            storage.Cleanup(assets); TempData["FeedbackDocumentError"] = "Files could not be saved. Please retry and check storage availability.";
            return RedirectToAction(nameof(Index));
        }
        catch { storage.Cleanup(assets); throw; }
    }

    [HttpGet, Authorize(Roles = "HR,Admin")]
    public async Task<IActionResult> Responses(int? id, string? search, string? department, string? status, CancellationToken cancellationToken = default)
    {
        if (!Management) return Forbid();
        id ??= await db.FeedbackDocuments.OrderByDescending(x => x.PublishedAtUtc).Select(x => (int?)x.Id).FirstOrDefaultAsync(cancellationToken);
        return id.HasValue ? RedirectToAction(nameof(Details), new { id, search, department, status }) : RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, string? search, string? department, string? status, CancellationToken cancellationToken = default)
    {
        if (search?.Length > 150 || department?.Length > 200) return BadRequest();
        var employeeId = await EmployeeIdAsync(cancellationToken);
        if (!Management && !employeeId.HasValue) return Forbid();
        var document = await db.FeedbackDocuments.AsNoTracking().Include(x => x.Assets.Where(asset => asset.SubmissionId == null))
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (document == null) return NotFound();
        var submissions = await db.FeedbackDocumentSubmissions.AsNoTracking().Where(x => x.DocumentId == id && (Management || x.EmployeeId == employeeId))
            .Include(x => x.Assets).ToListAsync(cancellationToken);
        var own = submissions.SingleOrDefault(x => x.EmployeeId == employeeId);
        var employees = Management ? await db.Employees.AsNoTracking().Where(x => x.IsActive
            || db.FeedbackDocumentSubmissions.Any(reply => reply.DocumentId == id && reply.EmployeeId == x.Id)).OrderBy(x => x.FullName).ToListAsync(cancellationToken) : [];
        var byEmployee = submissions.ToDictionary(x => x.EmployeeId);
        var rows = employees.Select(employee => new FeedbackDocumentEmployeeRow(employee, byEmployee.GetValueOrDefault(employee.Id))).ToList();
        IEnumerable<FeedbackDocumentEmployeeRow> filtered = rows;
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search)) filtered = filtered.Where(row => new[] { row.Employee.FullName, row.Employee.EmployeeCode, row.Employee.Department, row.Employee.Designation }
            .Any(value => value.Contains(search, StringComparison.OrdinalIgnoreCase)));
        if (!string.IsNullOrWhiteSpace(department)) filtered = filtered.Where(row => string.Equals(row.Employee.Department, department, StringComparison.OrdinalIgnoreCase));
        if (status == "submitted") filtered = filtered.Where(row => row.Submission != null);
        else if (status == "pending") filtered = filtered.Where(row => row.Submission == null);
        return View(new FeedbackDocumentDetailsViewModel { IsManagement = Management, CanSubmit = employeeId.HasValue,
            Document = document, OwnSubmission = own, Employees = filtered.ToList(), TotalEmployees = rows.Count,
            SubmittedCount = rows.Count(x => x.Submission != null), Search = search, Department = department, Status = status,
            Departments = rows.Select(x => x.Employee.Department).Distinct().Order().ToList(),
            Documents = Management ? await db.FeedbackDocuments.AsNoTracking().OrderByDescending(x => x.PublishedAtUtc).ToListAsync(cancellationToken) : [] });
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(27 * 1024 * 1024)]
    public async Task<IActionResult> Submit(int id, SubmitFeedbackDocumentForm form, CancellationToken cancellationToken)
    {
        var employeeId = await EmployeeIdAsync(cancellationToken);
        if (!employeeId.HasValue || !int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return Forbid();
        if (!await db.FeedbackDocuments.AnyAsync(x => x.Id == id, cancellationToken)) return NotFound();
        var error = await FeedbackDocumentStorage.ValidateAsync(form.Files);
        if (error != null) ModelState.AddModelError(nameof(form.Files), error);
        if (!ModelState.IsValid) return Failure(nameof(Details), id);
        var submission = await db.FeedbackDocumentSubmissions.SingleOrDefaultAsync(x => x.DocumentId == id && x.EmployeeId == employeeId, cancellationToken);
        if (submission == null)
        {
            submission = new FeedbackDocumentSubmission { DocumentId = id, EmployeeId = employeeId.Value };
            db.FeedbackDocumentSubmissions.Add(submission);
        }
        else submission.Revision++;
        submission.Comment = form.Comment?.Trim(); submission.SubmittedByUserId = actorId; submission.SubmittedAtUtc = DateTime.UtcNow;
        var storage = new FeedbackDocumentStorage(environment);
        List<FeedbackDocumentAsset> assets = [];
        try
        {
            assets = await storage.StoreAsync(form.Files, cancellationToken);
            foreach (var asset in assets) { asset.DocumentId = id; asset.Submission = submission; asset.Revision = submission.Revision; }
            db.FeedbackDocumentAssets.AddRange(assets);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException || exception is DbUpdateException { InnerException: PostgresException { SqlState: "23505" } })
        {
            storage.Cleanup(assets); TempData["FeedbackDocumentError"] = "Another upload was saved first. Refresh this page and retry your updated response.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            storage.Cleanup(assets); TempData["FeedbackDocumentError"] = "Files could not be saved. Please retry.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch { storage.Cleanup(assets); throw; }
        TempData["FeedbackDocumentMessage"] = "Your files have been submitted to HR and Admin.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> File(int id, bool download = false, CancellationToken cancellationToken = default)
    {
        var asset = await db.FeedbackDocumentAssets.AsNoTracking().Include(x => x.Submission).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (asset == null) return NotFound();
        var employeeId = Management ? null : await EmployeeIdAsync(cancellationToken);
        if (!FeedbackDocumentStorage.CanReadAsset(Management, employeeId, asset)
            || (asset.Submission != null && asset.Submission.DocumentId != asset.DocumentId)) return Forbid();
        if (Path.GetFileName(asset.StoredFileName) != asset.StoredFileName) return NotFound();
        var path = Path.Combine(new FeedbackDocumentStorage(environment).Folder, asset.StoredFileName);
        if (!System.IO.File.Exists(path)) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return download ? PhysicalFile(path, asset.ContentType, asset.OriginalFileName)
            : PhysicalFile(path, asset.ContentType, enableRangeProcessing: true);
    }
    private IActionResult Failure(string action, int? id = null)
    {
        TempData["FeedbackDocumentError"] = string.Join(" ", ModelState.Values.SelectMany(value => value.Errors).Select(error => error.ErrorMessage));
        return RedirectToAction(action, new { id });
    }
}
