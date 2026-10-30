using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;

namespace VertexERP.Services;

/// <summary>
/// Keeps employee photos outside the release directory in Production while
/// continuing to serve photos from the legacy wwwroot uploads folder.
/// </summary>
public sealed class EmployeePhotoStorageService : IDisposable
{
    public const string RequestPath = "/uploads/employees";
    private const string UrlPrefix = RequestPath + "/";
    private readonly string _storageRoot;
    private readonly string _legacyRoot;
    private readonly PhysicalFileProvider _storageProvider;
    private readonly PhysicalFileProvider _legacyProvider;

    public IFileProvider FileProvider { get; }

    public EmployeePhotoStorageService(IWebHostEnvironment environment, IConfiguration configuration)
    {
        var webRoot = string.IsNullOrWhiteSpace(environment.WebRootPath)
            ? Path.Combine(environment.ContentRootPath, "wwwroot")
            : environment.WebRootPath;
        _legacyRoot = Path.GetFullPath(Path.Combine(webRoot, "uploads", "employees"));

        var configuredRoot = configuration["EmployeePhotos:StoragePath"];
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            _storageRoot = Path.GetFullPath(configuredRoot);
        }
        else if (environment.IsProduction())
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(userProfile))
                throw new InvalidOperationException("Configure EmployeePhotos:StoragePath for Production employee photo storage.");
            _storageRoot = Path.GetFullPath(Path.Combine(userProfile, "erp-data", "employee-photos"));
        }
        else
        {
            _storageRoot = _legacyRoot;
        }

        Directory.CreateDirectory(_storageRoot);
        Directory.CreateDirectory(_legacyRoot);
        _storageProvider = new PhysicalFileProvider(_storageRoot);
        _legacyProvider = new PhysicalFileProvider(_legacyRoot);
        FileProvider = new CompositeFileProvider(_storageProvider, _legacyProvider);
    }

    public async Task<string> SaveAsync(IFormFile photo, string extension, CancellationToken cancellationToken = default)
    {
        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var destination = Path.Combine(_storageRoot, fileName);
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
        await photo.CopyToAsync(target, cancellationToken);
        return UrlPrefix + fileName;
    }

    public void Delete(string? photoUrl)
    {
        if (string.IsNullOrWhiteSpace(photoUrl) || !photoUrl.StartsWith(UrlPrefix, StringComparison.OrdinalIgnoreCase))
            return;

        var fileName = photoUrl[UrlPrefix.Length..];
        if (string.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName))
            return;

        foreach (var root in new[] { _storageRoot, _legacyRoot }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(root, fileName);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    public void Dispose()
    {
        _storageProvider.Dispose();
        _legacyProvider.Dispose();
    }
}
