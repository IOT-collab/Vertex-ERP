using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.FileProviders;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;
using Vertex_ERP.Controllers;

internal static class FeedbackDocumentChecks
{
    public static async Task Run()
    {
        var passed = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); passed++; Console.WriteLine("PASS " + label); }
        IFormFile Upload(string name, string mime, byte[] bytes, long? size = null) => new FormFile(new MemoryStream(bytes),0,size ?? bytes.Length,"Files",name) { Headers = new HeaderDictionary(), ContentType = mime };
        var pdf = Upload("form.pdf","application/pdf","%PDF-1.7"u8.ToArray());
        var png = Upload("response.png","image/png",new byte[] {137,80,78,71,13,10,26,10});
        Check(await FeedbackDocumentStorage.ValidateAsync(new[] { pdf, png }) == null, "PDF and PNG form/response files accepted");
        Check(await FeedbackDocumentStorage.ValidateAsync(new[] { Upload("photo.jpg","image/jpeg",new byte[] {255,216,255,0}) }) == null, "JPG responses accepted");
        Check(await FeedbackDocumentStorage.ValidateAsync([]) != null, "At least one file is required");
        Check(await FeedbackDocumentStorage.ValidateAsync(Enumerable.Repeat(pdf,6).ToList()) != null, "More than five files rejected");
        Check(await FeedbackDocumentStorage.ValidateAsync(new[] { Upload("big.pdf","application/pdf","%PDF-"u8.ToArray(),FeedbackDocumentStorage.MaximumFileSize + 1) }) != null, "Oversized individual files rejected");
        Check(await FeedbackDocumentStorage.ValidateAsync(Enumerable.Range(0,3).Select(x => Upload("file.pdf","application/pdf","%PDF-"u8.ToArray(),9*1024*1024)).ToList()) != null, "Combined upload limit enforced");
        Check(await FeedbackDocumentStorage.ValidateAsync(new[] { Upload("bad.pdf","application/pdf","<script>"u8.ToArray()) }) != null, "Renamed invalid PDF rejected");
        Check(await FeedbackDocumentStorage.ValidateAsync(new[] { Upload("file.pdf","image/png","%PDF-1.7"u8.ToArray()) }) != null, "MIME and extension mismatch rejected");
        Check(await FeedbackDocumentStorage.ValidateAsync(new[] { Upload("file.exe","application/octet-stream",new byte[] {1,2,3}) }) != null, "Executable uploads rejected");
        var source = new FeedbackDocumentAsset { Id=1, DocumentId=1 };
        var response = new FeedbackDocumentAsset { Id=2, DocumentId=1, SubmissionId=3, Submission=new FeedbackDocumentSubmission { Id=3,DocumentId=1,EmployeeId=7 } };
        Check(FeedbackDocumentStorage.CanReadAsset(false,7,source) && FeedbackDocumentStorage.CanReadAsset(false,8,source), "Published source documents available to all active employee identities");
        Check(FeedbackDocumentStorage.CanReadAsset(false,7,response), "Employee can access own response files");
        Check(!FeedbackDocumentStorage.CanReadAsset(false,8,response), "Other employees and managers cannot access private responses");
        Check(FeedbackDocumentStorage.CanReadAsset(true,null,response), "HR/Admin can access every submitted file");
        Check(!FeedbackDocumentStorage.CanReadAsset(false,null,source), "Unlinked caller cannot read published files");
        bool Valid(object form) => Validator.TryValidateObject(form,new ValidationContext(form),new List<ValidationResult>(),true);
        Check(!Valid(new PublishFeedbackDocumentForm { Title=" " }), "Blank publication title rejected");
        Check(!Valid(new PublishFeedbackDocumentForm { Title=new string('x',151) }), "Publication title length enforced");
        Check(!Valid(new SubmitFeedbackDocumentForm { Comment=new string('x',2001) }), "Response comment length enforced");
        var publish = typeof(FeedbackDocumentsController).GetMethod("Publish")!;
        Check(publish.GetCustomAttributes(typeof(AuthorizeAttribute),true).Cast<AuthorizeAttribute>().Single().Roles == "HR,Admin", "Publication restricted to HR/Admin");
        foreach (var name in new[] { "Publish", "Submit" }) {
            var method=typeof(FeedbackDocumentsController).GetMethod(name)!;
            Check(method.IsDefined(typeof(HttpPostAttribute),true) && method.IsDefined(typeof(ValidateAntiForgeryTokenAttribute),true), name + " requires POST and antiforgery");
        }
        Check(typeof(SubmitFeedbackDocumentForm).GetProperty("EmployeeId") == null && typeof(PublishFeedbackDocumentForm).GetProperty("PublishedByUserId") == null, "Posted forms cannot assign employee or publisher identity");
        var controller = new FeedbackDocumentsController(null!,null!) { ControllerContext=new ControllerContext { HttpContext=new DefaultHttpContext() } };
        Check(await controller.Publish(new(),default) is ForbidResult, "Unauthorized publication rejected before any database or file operation");
        Check(await controller.Responses(null,null,null,null) is ForbidResult, "Employee cannot open the HR response tracker");
        Check(await controller.Details(1,null,null,null) is ForbidResult, "Unlinked caller cannot open document details");
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
        var submissionEntity=db.Model.FindEntityType(typeof(FeedbackDocumentSubmission))!;
        Check(submissionEntity.FindProperty(nameof(FeedbackDocumentSubmission.Revision))!.IsConcurrencyToken, "Concurrent response edits protected by revision");
        Check(submissionEntity.GetIndexes().Any(index=>index.IsUnique && index.Properties.Select(p=>p.Name).SequenceEqual(new[]{"DocumentId","EmployeeId"})), "Only one submission identity per employee/form");
        var sql=db.GetService<IMigrator>().GenerateScript("20261027150000_ExpenseRecipientResponse","FeedbackDocumentExchange");
        Check(sql.Contains("CREATE TABLE \"FeedbackDocuments\"") && sql.Contains("CREATE TABLE \"FeedbackDocumentSubmissions\"") && sql.Contains("CREATE TABLE \"FeedbackDocumentAssets\""), "Migration creates publication, response and asset storage");
        Check(sql.Contains("REFERENCES \"Users\"") && !sql.Contains("DROP TABLE \"EmployeeFeedbacks\""), "Mapped Users table used and old feedback retained");
        var root=Path.GetFullPath("artifacts/feedback-storage-check");
        var storage=new FeedbackDocumentStorage(new TestEnvironment { ContentRootPath=root });
        var stored=await storage.StoreAsync(new[]{pdf,png},default);
        Check(stored.Count == 2 && stored.All(file=>System.IO.File.Exists(Path.Combine(storage.Folder,file.StoredFileName))) && stored.All(file=>file.StoredFileName != file.OriginalFileName), "Files saved privately with generated names");
        storage.Cleanup(stored);
        Check(stored.All(file=>!System.IO.File.Exists(Path.Combine(storage.Folder,file.StoredFileName))), "Failed-save cleanup removes only newly uploaded files");
        var cancelled=false;
        try { await storage.StoreAsync(new[]{pdf},new CancellationToken(true)); } catch(OperationCanceledException) { cancelled=true; }
        Check(cancelled && !Directory.EnumerateFiles(storage.Folder).Any(), "Cancelled uploads leave no orphaned file");
        await FeedbackDocumentViewChecks.Run();
        Console.WriteLine($"{passed} feedback document checks and four full page renders passed.");
    }
    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName {get;set;}="Test"; public string ApplicationName {get;set;}="Test";
        public string WebRootPath {get;set;}=""; public string ContentRootPath {get;set;}="";
        public IFileProvider WebRootFileProvider {get;set;}=new NullFileProvider(); public IFileProvider ContentRootFileProvider {get;set;}=new NullFileProvider();
    }
}
