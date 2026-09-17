using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VertexERP.Models;
using VertexERP.Services;
using VertexERP.Controllers;
using Vertex_ERP.Controllers;

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
foreach (long size in new long[] { 3 * 1024 * 1024 - 1, 3 * 1024 * 1024, 3 * 1024 * 1024 + 1, 16 * 1024 * 1024 })
{
    var model = new EmployeeDocumentUploadViewModel { EmployeeId = 1, DocumentType = "Other", DocumentName = "Boundary test", File = new FormFile(Stream.Null, 0, size, "File", "test.pdf") };
    var errors = new List<ValidationResult>();
    bool valid = Validator.TryValidateObject(model, new ValidationContext(model), errors, true);
    Check(valid == (size <= 3 * 1024 * 1024), $"Upload boundary {size} bytes");
    if (!valid) Check(errors.Any(error => error.MemberNames.Contains("File") && error.ErrorMessage!.Contains("3 MB")), "Oversize error identifies file and limit");
}
var update = new EmployeeDocumentUploadViewModel { DocumentId = 9, EmployeeId = 1, DocumentType = "PAN Card", DocumentName = "Updated title" };
var updateErrors = new List<ValidationResult>();
Check(Validator.TryValidateObject(update, new ValidationContext(update), updateErrors, true), "Document metadata update allows keeping existing file");
update.File = new FormFile(Stream.Null, 0, 3 * 1024 * 1024 + 1, "File", "replacement.pdf");
Check(!Validator.TryValidateObject(update, new ValidationContext(update), new List<ValidationResult>(), true), "Replacement file above 3 MB rejected");
update.DocumentId = null; update.File = null;
Check(!Validator.TryValidateObject(update, new ValidationContext(update), new List<ValidationResult>(), true), "New upload requires a file");
update.File = new FormFile(Stream.Null, 0, 0, "File", "empty.pdf");
Check(!Validator.TryValidateObject(update, new ValidationContext(update), new List<ValidationResult>(), true), "Empty upload rejected");
update.DocumentId = 9;
Check(!Validator.TryValidateObject(update, new ValidationContext(update), new List<ValidationResult>(), true), "Empty replacement rejected");
var repository = new EmployeeDocumentRepositoryViewModel { Documents = new[] {
    new EmployeeDocument { DocumentType = "PAN Card" }, new EmployeeDocument { DocumentType = "PAN Card" },
    new EmployeeDocument { DocumentType = "Aadhaar Card" }
} };
Check(repository.UploadedTypeCount == 2, "Checklist counts types once when multiple files have the same type");
Check(EmployeeDocumentUploadViewModel.DocumentTypes.Count - repository.UploadedTypeCount == 8, "Checklist reports remaining types as not uploaded");
Check(new EmployeeDocumentRepositoryViewModel().UploadedTypeCount == 0, "Employee without documents has zero uploaded types");
var employee = new Employee { Id = 17, FirstName = "Test", FullName = "Test Employee" };
var generated = EmployeeCredentialService.Generate("Test", "Employee");
var account = new AppUser();
EmployeeCredentialService.Apply(account, employee, generated.Username, generated.Password);
Check(account.Employee == employee && account.Role == "Employee", "Generated account maps to employee and existing employee role");
Check(PasswordHashService.VerifyPassword(generated.Password, account.PasswordHash), "Generated password works with existing login verifier");
var oldHash = account.PasswordHash;
account.Role = "Manager";
EmployeeCredentialService.Apply(account, employee, "Updated.User", null);
Check(account.Username == "Updated.User" && account.NormalizedUsername == "UPDATED.USER", "Username update uses existing login normalization");
Check(account.PasswordHash == oldHash && account.Role == "Manager", "Blank password preserves password and manager role");
EmployeeCredentialService.Apply(account, employee, "Updated.User", " NewPassword123! ");
Check(PasswordHashService.VerifyPassword(" NewPassword123! ", account.PasswordHash), "Updated password preserves exact characters");
Check(!PasswordHashService.VerifyPassword(generated.Password, account.PasswordHash), "Old password fails after password update");
foreach (var (property, value) in new[] { ("LoginUsername", "ab"), ("LoginUsername", "bad username"), ("TemporaryPassword", "short") })
{
    var errors = new List<ValidationResult>();
    Check(!Validator.TryValidateProperty(value, new ValidationContext(new EmployeeFormViewModel()) { MemberName = property }, errors), $"Invalid {property} rejected");
}
Check(typeof(HrController).GetMethod("DeleteDocument")!.IsDefined(typeof(HttpPostAttribute), true), "Document deletion requires POST");
Check(typeof(HrController).GetMethod("DeleteDocument")!.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), true), "Document deletion requires antiforgery token");
foreach (var controller in new[] { typeof(HrController), typeof(EmployeeController) })
    Check(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Any(attribute => attribute.Roles == "Admin,HR"), $"{controller.Name} restricted to HR/Admin");
Console.WriteLine($"{passed} checks passed.");
