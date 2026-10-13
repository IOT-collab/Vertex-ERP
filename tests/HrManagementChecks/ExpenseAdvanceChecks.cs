using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VertexERP.Data;
using VertexERP.Models;
using Vertex_ERP.Controllers;

static class ExpenseAdvanceChecks
{
    public static async Task Run()
    {
        var passed = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new Exception(name);
            Console.WriteLine("PASS " + name); passed++;
        }
        bool Valid(object model) => Validator.TryValidateObject(model, new ValidationContext(model), new List<ValidationResult>(), true);
        var model = new RecordExpenseAdvanceViewModel { EmployeeId = 1, Purpose = "Site visit", Amount = 1000, PaymentMode = "UPI", TransactionId = "TX-123", PaidAtIst = DateTime.Now };
        Check(!Valid(model), "Payment proof is mandatory");
        model.ReceiptFile = File("receipt.pdf", "%PDF-1.7"u8.ToArray());
        Check(Valid(model), "Complete payment record passes model validation");
        model.Amount = 0; Check(!Valid(model), "Zero amount rejected"); model.Amount = 1000;
        model.TransactionId = " "; Check(!Valid(model), "Blank transaction ID rejected"); model.TransactionId = "TX-123";
        model.PaidAtIst = null; Check(!Valid(model), "Payment date and time required");
        var signature = typeof(ExpenseController).GetMethod("HasReceiptSignatureAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        async Task<bool> Signature(string name, byte[] bytes) => await (Task<bool>)signature.Invoke(null, new object[] { File(name, bytes) })!;
        Check(await Signature("receipt.pdf", "%PDF-1.7"u8.ToArray()), "PDF signature accepted");
        Check(await Signature("receipt.jpg", new byte[] { 255, 216, 255, 0 }), "JPEG signature accepted");
        Check(await Signature("receipt.png", new byte[] {137,80,78,71,13,10,26,10}), "PNG signature accepted");
        Check(!await Signature("receipt.pdf", "<script>"u8.ToArray()), "Renamed non-PDF file rejected");
        Check(!await Signature("receipt.png", new byte[] {137,80,78}), "Truncated PNG header rejected");
        foreach (var action in new[] { "RecordAdvance", "ConfirmAdvance" })
        {
            var method = typeof(ExpenseController).GetMethod(action)!;
            Check(method.IsDefined(typeof(HttpPostAttribute)) && method.IsDefined(typeof(ValidateAntiForgeryTokenAttribute)), action + " requires POST and antiforgery token");
        }
        Check(typeof(ExpenseController).GetMethod("RecordAdvance")!.GetCustomAttribute<AuthorizeAttribute>()!.Roles == "HR,Admin", "Only HR/Admin can record payments");
        var controller = new ExpenseController(null!, null!) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) } } };
        Check(await controller.ConfirmAdvance(new() { Id = 1, Response = "Received" }) is ForbidResult, "Unlinked caller cannot confirm payment");
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
        var entity = db.Model.FindEntityType(typeof(ExpenseAdvance))!;
        Check(entity.FindProperty(nameof(ExpenseAdvance.ConfirmationStatus))!.IsConcurrencyToken, "Concurrent confirmation changes are protected");
        Check(entity.FindProperty(nameof(ExpenseAdvance.Amount))!.GetScale() == 2, "Payment amounts use two decimal places");
        Check(db.Database.GenerateCreateScript().Contains("CREATE TABLE \"ExpenseAdvances\""), "Database model includes advance payments");
        Console.WriteLine($"{passed} expense advance checks passed.");
    }

    static IFormFile File(string name, byte[] data) => new FormFile(new MemoryStream(data), 0, data.Length, "ReceiptFile", name) { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
}
