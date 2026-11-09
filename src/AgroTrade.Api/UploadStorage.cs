using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace AgroTrade.Api;

public sealed class UploadStorage
{
    private readonly IWebHostEnvironment environment;
    private readonly string? publicBaseUrl;
    public string RootPath { get; }

    public UploadStorage(IConfiguration configuration, IWebHostEnvironment environment)
    {
        this.environment = environment;
        var configuredPath = configuration["Storage:UploadsPath"];
        configuredPath = string.IsNullOrWhiteSpace(configuredPath)
            ? OperatingSystem.IsWindows() ? @"C:\AgroTradeData\uploads" : "/var/lib/agrotrade/uploads"
            : configuredPath;
        if (!Path.IsPathFullyQualified(configuredPath))
            throw new InvalidOperationException("Storage:UploadsPath must be an absolute directory outside the application.");
        RootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuredPath));
        var contentRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(environment.ContentRootPath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (RootPath.Equals(Path.GetPathRoot(RootPath), comparison) || RootPath.Equals(contentRoot, comparison) ||
            RootPath.StartsWith(contentRoot + Path.DirectorySeparatorChar, comparison))
            throw new InvalidOperationException("Upload storage must be outside the application and cannot be a drive root.");
        publicBaseUrl = configuration["Storage:PublicBaseUrl"]?.Trim().TrimEnd('/');
    }

    public int Initialize()
    {
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(Path.Combine(RootPath, "products"));
        Directory.CreateDirectory(Path.Combine(RootPath, "categories"));
        var legacyPath = Path.Combine(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), "uploads");
        if (!Directory.Exists(legacyPath)) return 0;
        var copied = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        foreach (var source in Directory.EnumerateFiles(legacyPath, "*", options))
        {
            if (Path.GetFileName(source) == ".gitkeep") continue;
            var target = Path.Combine(RootPath, Path.GetRelativePath(legacyPath, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target))
            {
                using var sourceStream = File.OpenRead(source);
                using var targetStream = File.OpenRead(target);
                if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(sourceStream), SHA256.HashData(targetStream)))
                    throw new IOException($"Upload migration conflict: {Path.GetRelativePath(legacyPath, source)}. Existing files were not overwritten.");
                continue;
            }
            File.Copy(source, target, overwrite: false);
            copied++;
        }
        return copied;
    }

    public async Task<string> SaveImageAsync(string folder, IFormFile image, HttpRequest request, CancellationToken cancellationToken)
    {
        if (folder is not "products" and not "categories") throw new ArgumentException("Unsupported image folder.");
        var extension = Path.GetExtension(image.FileName).ToLowerInvariant();
        if (image.Length == 0 || extension is not ".jpg" and not ".jpeg" and not ".png" and not ".webp" and not ".gif")
            throw new InvalidOperationException("Unsupported image file.");
        var directory = Path.Combine(RootPath, folder);
        Directory.CreateDirectory(directory);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var target = Path.Combine(directory, fileName);
        var temporary = target + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await image.CopyToAsync(stream, cancellationToken);
            File.Move(temporary, target);
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
        var baseUrl = string.IsNullOrWhiteSpace(publicBaseUrl) ? $"{request.Scheme}://{request.Host}{request.PathBase}" : publicBaseUrl;
        return $"{baseUrl}/uploads/{folder}/{fileName}";
    }
}
