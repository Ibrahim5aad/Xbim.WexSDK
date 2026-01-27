using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octopus.Server.Abstractions.Storage;

namespace Octopus.Server.Storage.LocalDisk;

/// <summary>
/// Local disk implementation of IStorageProvider.
/// Stores files on the local filesystem with workspace isolation via subdirectories.
/// Suitable for development and single-server deployments.
/// </summary>
public class LocalDiskStorageProvider : IStorageProvider
{
    private readonly LocalDiskStorageOptions _options;
    private readonly ILogger<LocalDiskStorageProvider> _logger;
    private readonly string _basePath;
    private bool _directoryCreated;

    public LocalDiskStorageProvider(
        IOptions<LocalDiskStorageOptions> options,
        ILogger<LocalDiskStorageProvider> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Resolve the base path (could be relative or absolute)
        _basePath = Path.IsPathRooted(_options.BasePath)
            ? _options.BasePath
            : Path.Combine(Directory.GetCurrentDirectory(), _options.BasePath);
    }

    public string ProviderId => "LocalDisk";

    public bool SupportsDirectUpload => false;

    private void EnsureDirectoryExists(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            _logger.LogDebug("Created directory {Directory}", directory);
        }
    }

    private void EnsureBaseDirectoryExists()
    {
        if (_directoryCreated || !_options.CreateDirectoryIfNotExists)
            return;

        if (!Directory.Exists(_basePath))
        {
            Directory.CreateDirectory(_basePath);
            _logger.LogDebug("Created base storage directory {BasePath}", _basePath);
        }

        _directoryCreated = true;
    }

    private string GetFullPath(string key)
    {
        // Sanitize key to prevent directory traversal attacks
        // 1. Replace path traversal sequences
        // 2. Remove any leading path separators to prevent root access
        // 3. Verify the final path is within the base path
        var sanitizedKey = key
            .Replace("..", string.Empty)
            .Replace(":", string.Empty) // Remove drive letters on Windows
            .TrimStart(Path.DirectorySeparatorChar)
            .TrimStart(Path.AltDirectorySeparatorChar);

        // Combine and get the absolute path
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, sanitizedKey));

        // Security check: ensure the path is within the base directory
        var normalizedBasePath = Path.GetFullPath(_basePath);
        if (!fullPath.StartsWith(normalizedBasePath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Attempted path traversal attack with key: {Key}", key);
            throw new InvalidOperationException($"Invalid storage key: path traversal attempt detected.");
        }

        return fullPath;
    }

    /// <inheritdoc />
    public async Task<string> PutAsync(
        string key,
        Stream content,
        string? contentType = null,
        CancellationToken cancellationToken = default)
    {
        EnsureBaseDirectoryExists();

        var fullPath = GetFullPath(key);

        // Concurrency safety: fail if file already exists
        if (File.Exists(fullPath))
        {
            _logger.LogWarning("File {FilePath} already exists, upload rejected for concurrency safety", fullPath);
            throw new InvalidOperationException($"File '{key}' already exists. Use a unique key to avoid conflicts.");
        }

        EnsureDirectoryExists(fullPath);

        // Write the file
        await using var fileStream = new FileStream(
            fullPath,
            FileMode.CreateNew, // CreateNew fails if file exists (additional safety)
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        await content.CopyToAsync(fileStream, cancellationToken);

        _logger.LogDebug("Stored file at {FilePath}", fullPath);

        // Store content type in a sidecar file if provided
        if (!string.IsNullOrEmpty(contentType))
        {
            var metadataPath = fullPath + ".meta";
            await File.WriteAllTextAsync(metadataPath, contentType, cancellationToken);
        }

        return key;
    }

    /// <inheritdoc />
    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        EnsureBaseDirectoryExists();

        var fullPath = GetFullPath(key);

        if (!File.Exists(fullPath))
        {
            _logger.LogDebug("File {FilePath} not found", fullPath);
            return Task.FromResult<Stream?>(null);
        }

        // Return a stream that supports async operations
        var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        _logger.LogDebug("Opened read stream for file {FilePath}", fullPath);
        return Task.FromResult<Stream?>(stream);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        EnsureBaseDirectoryExists();

        var fullPath = GetFullPath(key);

        if (!File.Exists(fullPath))
        {
            _logger.LogDebug("File {FilePath} not found for deletion", fullPath);
            return Task.FromResult(false);
        }

        File.Delete(fullPath);

        // Also delete metadata sidecar if exists
        var metadataPath = fullPath + ".meta";
        if (File.Exists(metadataPath))
        {
            File.Delete(metadataPath);
        }

        _logger.LogDebug("Deleted file {FilePath}", fullPath);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        EnsureBaseDirectoryExists();

        var fullPath = GetFullPath(key);
        return Task.FromResult(File.Exists(fullPath));
    }

    /// <inheritdoc />
    public Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken = default)
    {
        EnsureBaseDirectoryExists();

        var fullPath = GetFullPath(key);

        if (!File.Exists(fullPath))
        {
            return Task.FromResult<long?>(null);
        }

        var fileInfo = new FileInfo(fullPath);
        return Task.FromResult<long?>(fileInfo.Length);
    }

    /// <inheritdoc />
    public Task<string?> GenerateUploadSasUrlAsync(
        string key,
        string? contentType,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        // Local disk storage does not support direct uploads via SAS URLs
        _logger.LogDebug("Direct upload not supported for LocalDisk storage provider");
        return Task.FromResult<string?>(null);
    }
}
