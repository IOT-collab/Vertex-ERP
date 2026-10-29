using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using VertexERP.Models;
using VertexERP.Services;
using Vertex_ERP.Controllers;

internal static class EmployeeDocumentActionChecks
{
    public static async Task Run()
    {
        var passed=0;void Check(bool ok,string name){if(!ok)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
        IFormFile Upload(string name,byte[] bytes,long? size=null)=>new FormFile(new MemoryStream(bytes),0,size??bytes.Length,"File",name);
        var pdf=Upload("aadhaar.pdf","%PDF-1.7\nexample"u8.ToArray());
        Check(await EmployeeDocumentStorage.ValidateAsync(pdf)==null,"PDF accepted");
        Check(await EmployeeDocumentStorage.ValidateAsync(Upload("photo.JPG",new byte[]{255,216,255,0}))==null,"JPG accepted case-insensitively");
        Check(await EmployeeDocumentStorage.ValidateAsync(Upload("photo.png",new byte[]{137,80,78,71,13,10,26,10}))==null,"PNG accepted");
        Check(await EmployeeDocumentStorage.ValidateAsync(Upload("letter.doc",new byte[]{208,207,17,224,161,177,26,225}))==null,"Word DOC accepted");
        Check(await EmployeeDocumentStorage.ValidateAsync(Upload("letter.docx",new byte[]{80,75,3,4}))==null,"Word DOCX accepted");
        Check(await EmployeeDocumentStorage.ValidateAsync(Upload("empty.pdf",[]))!=null,"Empty upload rejected");
        Check(await EmployeeDocumentStorage.ValidateAsync(Upload("large.pdf","%PDF-"u8.ToArray(),EmployeeDocumentUploadViewModel.MaximumFileSize+1))!=null,"Three MB limit enforced on server");
        Check(await EmployeeDocumentStorage.ValidateAsync(Upload("bad.pdf","<html>bad"u8.ToArray()))!=null,"Renamed invalid PDF rejected");
        Check(await EmployeeDocumentStorage.ValidateAsync(Upload("bad.exe",new byte[]{1,2,3}))!=null,"Unsupported file type rejected");
        Check(EmployeeDocumentStorage.OriginalName("C:\\fakepath\\aadhaar.pdf")=="aadhaar.pdf","Browser fake path removed from original filename");
        Check(EmployeeDocumentStorage.ContentType("file.PDF")=="application/pdf" && EmployeeDocumentStorage.ContentType("unknown")=="application/octet-stream","Content types use safe known mappings");
        var parent=Path.GetFullPath("artifacts/document-storage-checks");var root=Path.Combine(parent,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var environment=new DocumentEnvironment{ContentRootPath=root,WebRootPath=root};var storage=new EmployeeDocumentStorage(environment);
        try {
            var stored=await storage.SaveAsync(pdf,default);
            Check(storage.Exists(stored) && stored!="aadhaar.pdf","Upload stored privately using generated filename");
            await using(var stream=storage.OpenRead(stored)) using(var reader=new StreamReader(stream)) Check((await reader.ReadToEndAsync()).StartsWith("%PDF-"),"View stream reads exact stored content");
            Check(!storage.Exists("../outside.pdf") && !storage.TryDelete("../outside.pdf"),"Unsafe storage paths cannot be opened or deleted");
            Check(!storage.Exists("C:\\outside.pdf") && !storage.Exists("..\\outside.pdf"),"Windows absolute and backslash traversal rejected");
            try{using var missing=storage.OpenRead("missing.pdf");Check(false,"Missing file must fail predictably");}catch(FileNotFoundException){Check(true,"Missing file detected before HTTP response starts");}
            var replacement=await storage.SaveAsync(Upload("replacement.pdf","%PDF-1.7 new"u8.ToArray()),default);
            Check(storage.Exists(stored)&&storage.Exists(replacement),"Replacement upload keeps original file until database save succeeds");
            Check(storage.TryDelete(replacement)&&storage.Exists(stored),"Failed-save cleanup removes replacement only");
            using var cancelled=new CancellationTokenSource();cancelled.Cancel();
            try{await storage.SaveAsync(pdf,cancelled.Token);Check(false,"Cancelled copy should fail");}catch(OperationCanceledException){Check(Directory.GetFiles(storage.Folder).Length==1,"Interrupted copy leaves no partial file");}
            Check(storage.TryDelete(stored)&&!storage.Exists(stored),"Stored document deletes successfully");
            Check(storage.TryDelete(stored),"Deleting already missing file is harmless");
            var context=new DefaultHttpContext();
            var controller=new HrController(null!,environment,NullLogger<HrController>.Instance,null!){ControllerContext=new ControllerContext{HttpContext=context},TempData=new TempDataDictionary(context,new DocumentTempData())};
            Check(await controller.ViewDocument(0,employeeId:117) is RedirectToActionResult {ActionName:"EmpDocuments"},"Invalid view link returns to document list");
            Check(await controller.UpdateDocument(0,117) is RedirectToActionResult {ActionName:"EmpDocuments"},"Invalid update link returns to document list");
            Check(await controller.DeleteDocument(0,117) is RedirectToActionResult {ActionName:"EmpDocuments"},"Repeated or invalid delete link returns to document list");
            Check(typeof(HrController).GetMethod("DeleteDocument")!.IsDefined(typeof(ValidateAntiForgeryTokenAttribute),true),"Delete remains antiforgery protected");
            Check(typeof(HrController).GetCustomAttributes(typeof(AuthorizeAttribute),true).Cast<AuthorizeAttribute>().Single().Roles=="Admin,HR","Private document actions remain HR/Admin only");
            var update=new EmployeeDocumentUploadViewModel{DocumentId=1,EmployeeId=8,DocumentType="Aadhaar Card",DocumentName="Updated title"};
            Check(!update.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(update)).Any(),"Existing document metadata can be updated without replacement upload");
        }
        finally { if(!Path.GetFullPath(root).StartsWith(parent+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Test cleanup path escaped its directory");Directory.Delete(root,true); }
        await EmployeeDocumentActionViewChecks.Run();
        Console.WriteLine($"{passed} employee document checks and view renders passed.");
    }
    private sealed class DocumentEnvironment:IWebHostEnvironment
    {
        public string ApplicationName{get;set;}="DocumentCheck";public string EnvironmentName{get;set;}="Development";
        public string ContentRootPath{get;set;}="";public string WebRootPath{get;set;}="";
        public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
    }
    private sealed class DocumentTempData:ITempDataProvider
    {
        public IDictionary<string,object> LoadTempData(HttpContext context)=>new Dictionary<string,object>();
        public void SaveTempData(HttpContext context,IDictionary<string,object> values){}
    }
}
