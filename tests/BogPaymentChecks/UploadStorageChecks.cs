using AgroTrade.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

static class UploadStorageChecks
{
    public static async Task RunAsync()
    {
        var fixture = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "storage-check", Guid.NewGuid().ToString("N"));
        var appRoot = Path.Combine(fixture, "app");
        var webRoot = Path.Combine(appRoot, "wwwroot");
        var dataRoot = Path.Combine(fixture, "data", "uploads");
        var oldFolder = Path.Combine(webRoot, "uploads", "products");
        Directory.CreateDirectory(oldFolder);
        var oldFile = Path.Combine(oldFolder, "existing.png");
        await File.WriteAllBytesAsync(oldFile, [1, 2, 3]);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:UploadsPath"] = dataRoot,
            ["Storage:PublicBaseUrl"] = "https://api.fixtop.ge"
        }).Build();
        var env = new TestEnvironment { ContentRootPath = appRoot, WebRootPath = webRoot };
        var storage = new UploadStorage(config, env);
        if (storage.Initialize() != 1 || storage.Initialize() != 0 || !File.Exists(oldFile))
            throw new Exception("Legacy copying must be repeatable and preserve the original.");
        using var bytes = new MemoryStream([4, 5, 6]);
        var image = new FormFile(bytes, 0, bytes.Length, "image", "photo.PNG");
        var result = await storage.SaveImageAsync("categories", image, new DefaultHttpContext().Request, default);
        if (!result.StartsWith("https://api.fixtop.ge/uploads/categories/") || !File.Exists(Path.Combine(dataRoot, "categories", Path.GetFileName(result))))
            throw new Exception("External upload path or public URL incorrect.");
        await File.WriteAllBytesAsync(Path.Combine(dataRoot, "products", "existing.png"), [9]);
        try { storage.Initialize(); throw new Exception("Conflicting file was overwritten."); }
        catch (IOException) { }
        config["Storage:UploadsPath"] = Path.Combine(appRoot, "files");
        try { _ = new UploadStorage(config, env); throw new Exception("In-project upload storage accepted."); }
        catch (InvalidOperationException) { }
        Console.WriteLine("Upload storage checks passed: external writes, stable URLs, repeatable copy, preserved originals and conflict protection.");
    }

    sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "StorageTests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = "";
        public string WebRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
