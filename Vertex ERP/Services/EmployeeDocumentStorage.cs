using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using VertexERP.Models;
namespace VertexERP.Services;

public sealed class EmployeeDocumentStorage(IWebHostEnvironment environment)
{
    public string Folder => Path.Combine(environment.ContentRootPath,"App_Data","EmployeeDocuments");
    public static readonly IReadOnlyDictionary<string,string> Types = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
        [".pdf"]="application/pdf",[".jpg"]="image/jpeg",[".jpeg"]="image/jpeg",[".png"]="image/png",[".doc"]="application/msword",[".docx"]="application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    };
    public string PathFor(string name)
    {
        if(string.IsNullOrWhiteSpace(name)||name is "." or ".."||name.IndexOfAny(new[]{'/','\\'})>=0||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||Path.IsPathRooted(name)) throw new InvalidDataException("Invalid document storage name.");
        return Path.Combine(Path.GetFullPath(Folder),name);
    }
    public bool Exists(string name) { try{return System.IO.File.Exists(PathFor(name));}catch(Exception e) when(e is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException){return false;} }
    public FileStream OpenRead(string name) => new(PathFor(name),FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous);
    public bool TryDelete(string? name)
    {
        if(string.IsNullOrEmpty(name))return true;
        try{System.IO.File.Delete(PathFor(name));return true;}
        catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException){return false;}
    }
    public static string ContentType(string name) => Types.GetValueOrDefault(Path.GetExtension(name),"application/octet-stream");
    public static string OriginalName(string name) => Path.GetFileName(name.Replace('\\','/')).Replace("\r","").Replace("\n","");
    public static async Task<string?> ValidateAsync(IFormFile file)
    {
        if(file.Length<=0)return "Choose a non-empty file.";
        if(file.Length>EmployeeDocumentUploadViewModel.MaximumFileSize)return "The file exceeds the 3 MB limit.";
        var name=OriginalName(file.FileName);
        if(string.IsNullOrWhiteSpace(name)||name.Length>260)return "Use a filename of at most 260 characters.";
        var extension=Path.GetExtension(name).ToLowerInvariant();
        if(!Types.ContainsKey(extension))return "Allowed formats: PDF, JPG, PNG, DOC and DOCX.";
        var header=new byte[8];await using var stream=file.OpenReadStream();var length=await stream.ReadAtLeastAsync(header,8,throwOnEndOfStream:false);
        var valid=extension switch {
            ".pdf" => length>=5 && header.AsSpan(0,5).SequenceEqual("%PDF-"u8),
            ".jpg" or ".jpeg" => length>=3 && header[0]==255 && header[1]==216 && header[2]==255,
            ".png" => length==8 && header.SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}),
            ".doc" => length==8 && header.SequenceEqual(new byte[]{208,207,17,224,161,177,26,225}),
            ".docx" => length>=4 && header[0]==80 && header[1]==75 && header[2]==3 && header[3]==4,
            _=>false
        };
        return valid?null:"The file content does not match its extension. Choose a valid PDF, image or Word document.";
    }
    public async Task<string> SaveAsync(IFormFile file,CancellationToken token)
    {
        Directory.CreateDirectory(Folder);
        var name=Guid.NewGuid().ToString("N")+Path.GetExtension(OriginalName(file.FileName)).ToLowerInvariant();
        try { await using(var target=new FileStream(PathFor(name),FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous)) await file.CopyToAsync(target,token); return name; }
        catch { TryDelete(name);throw; }
    }
}
