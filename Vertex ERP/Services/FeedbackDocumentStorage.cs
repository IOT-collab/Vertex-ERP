using VertexERP.Models;

namespace VertexERP.Services;

public sealed class FeedbackDocumentStorage(IWebHostEnvironment environment)
{
    public const long MaximumFileSize = 10 * 1024 * 1024;
    public const long MaximumTotalSize = 25 * 1024 * 1024;
    public static readonly IReadOnlyDictionary<string,string> Types = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
        [".pdf"] = "application/pdf", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png"
    };
    public string Folder => Path.Combine(environment.ContentRootPath, "App_Data", "FeedbackDocuments");
    public static async Task<string?> ValidateAsync(IReadOnlyList<IFormFile>? files)
    {
        if (files == null || files.Count == 0) return "Choose at least one PDF or image.";
        if (files.Count > 5) return "You can upload up to five files at a time.";
        if (files.Sum(x => x.Length) > MaximumTotalSize) return "Combined file size must be 25 MB or smaller.";
        foreach (var file in files)
        {
            if (file.Length <= 0 || file.Length > MaximumFileSize) return "Each file must be non-empty and 10 MB or smaller.";
            if (Path.GetFileName(file.FileName).Length > 260) return "A filename is too long.";
            var extension = Path.GetExtension(file.FileName);
            if (!Types.TryGetValue(extension, out var contentType) || !string.Equals(contentType, file.ContentType, StringComparison.OrdinalIgnoreCase))
                return "Only PDF, JPG and PNG files are allowed.";
            await using var stream = file.OpenReadStream();
            var header = new byte[8];
            var count = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false);
            var valid = extension.ToLowerInvariant() switch {
                ".pdf" => count >= 5 && header.AsSpan(0,5).SequenceEqual("%PDF-"u8),
                ".jpg" or ".jpeg" => count >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255,
                ".png" => count == 8 && header.SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}), _ => false
            };
            if (!valid) return "File content must match its PDF or image type.";
        }
        return null;
    }
    public async Task<List<FeedbackDocumentAsset>> StoreAsync(IEnumerable<IFormFile> files, CancellationToken token)
    {
        var saved = new List<FeedbackDocumentAsset>();
        try
        {
            Directory.CreateDirectory(Folder);
            foreach (var file in files)
            {
                var asset = new FeedbackDocumentAsset { StoredFileName = Guid.NewGuid().ToString("N") + Path.GetExtension(file.FileName).ToLowerInvariant(),
                    OriginalFileName = Path.GetFileName(file.FileName), ContentType = Types[Path.GetExtension(file.FileName)], FileSize = file.Length };
                saved.Add(asset);
                await using var target = new FileStream(Path.Combine(Folder, asset.StoredFileName), FileMode.CreateNew);
                await file.CopyToAsync(target, token);
            }
            return saved;
        }
        catch { Cleanup(saved); throw; }
    }
    public void Cleanup(IEnumerable<FeedbackDocumentAsset> assets)
    {
        foreach (var asset in assets)
        {
            if (Path.GetFileName(asset.StoredFileName) != asset.StoredFileName) continue;
            try { System.IO.File.Delete(Path.Combine(Folder, asset.StoredFileName)); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    public static bool CanReadAsset(bool management, int? viewerEmployeeId, FeedbackDocumentAsset asset) =>
        management || (viewerEmployeeId.HasValue && (asset.SubmissionId == null || asset.Submission?.EmployeeId == viewerEmployeeId));
}
