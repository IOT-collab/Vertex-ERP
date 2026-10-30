using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using VertexERP.Services;

public static class EmployeePhotoStorageChecks
{
    public static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "vertex-photo-check-" + Guid.NewGuid().ToString("N"));
        var webRoot = Path.Combine(root, "wwwroot");
        var persistentRoot = Path.Combine(root, "persistent-photos");
        Directory.CreateDirectory(webRoot);
        try
        {
            var environment = new TestWebHostEnvironment
            {
                EnvironmentName = "Development",
                ContentRootPath = root,
                WebRootPath = webRoot,
                WebRootFileProvider = new PhysicalFileProvider(webRoot)
            };
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmployeePhotos:StoragePath"] = persistentRoot
            }).Build();

            using var storage = new EmployeePhotoStorageService(environment, configuration);
            await using var content = new MemoryStream([1, 2, 3, 4]);
            var file = new FormFile(content, 0, content.Length, "photo", "employee.png");
            var savedUrl = await storage.SaveAsync(file, ".png");
            var fileName = Path.GetFileName(savedUrl);
            Check(savedUrl.StartsWith("/uploads/employees/", StringComparison.Ordinal), "Employee photo keeps the existing public URL");
            Check(File.Exists(Path.Combine(persistentRoot, fileName)), "New employee photo is saved in persistent storage");
            Check(storage.FileProvider.GetFileInfo(fileName).Exists, "Persistent photo is served through the existing URL provider");

            var legacyName = "legacy-photo.png";
            await File.WriteAllBytesAsync(Path.Combine(webRoot, "uploads", "employees", legacyName), [5, 6, 7]);
            Check(storage.FileProvider.GetFileInfo(legacyName).Exists, "Legacy wwwroot employee photos remain readable");

            storage.Delete(savedUrl);
            storage.Delete("/uploads/employees/" + legacyName);
            Check(!File.Exists(Path.Combine(persistentRoot, fileName)), "Replacing a photo removes its persistent predecessor");
            Check(!File.Exists(Path.Combine(webRoot, "uploads", "employees", legacyName)), "Legacy photo cleanup remains supported");
            Console.WriteLine("PASS: Employee photo persistence and legacy URL compatibility");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "VertexERP.PhotoChecks";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
