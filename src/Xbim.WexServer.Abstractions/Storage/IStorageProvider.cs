namespace Xbim.WexServer.Abstractions.Storage;

/// <summary>
/// Abstraction for blob/file storage operations.
/// </summary>
public interface IStorageProvider
{
    /// <summary>
    /// Gets the unique identifier for this storage provider (e.g., "LocalDisk", "AzureBlob").
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Writes content to storage.
    /// </summary>
    /// <param name="key">The storage key (path/identifier).</param>
    /// <param name="content">The content stream to write.</param>
    /// <param name="contentType">Optional content type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The storage key used.</returns>
    Task<string> PutAsync(string key, Stream content, string? contentType = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a stream for reading content from storage.
    /// </summary>
    /// <param name="key">The storage key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A readable stream, or null if not found.</returns>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes content from storage.
    /// </summary>
    /// <param name="key">The storage key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted, false if not found.</returns>
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if content exists in storage.
    /// </summary>
    /// <param name="key">The storage key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if exists.</returns>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the size of content in storage.
    /// </summary>
    /// <param name="key">The storage key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Size in bytes, or null if not found.</returns>
    Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a time-limited URL for direct client uploads (e.g., SAS URL for Azure Blob).
    /// </summary>
    /// <param name="key">The storage key where the upload should be written.</param>
    /// <param name="contentType">Optional content type for the upload.</param>
    /// <param name="expiresAt">When the upload URL should expire.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The upload URL, or null if direct uploads are not supported.</returns>
    Task<string?> GenerateUploadSasUrlAsync(
        string key,
        string? contentType,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Indicates whether this provider supports direct client uploads via SAS URLs.
    /// </summary>
    bool SupportsDirectUpload { get; }

    /// <summary>
    /// Checks if the storage provider is healthy and accessible.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the storage is healthy, false otherwise.</returns>
    Task<StorageHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a storage health check.
/// </summary>
public record StorageHealthResult
{
    /// <summary>
    /// Whether the storage is healthy.
    /// </summary>
    public required bool IsHealthy { get; init; }

    /// <summary>
    /// Optional message describing the health status or any issues.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Optional additional data about the storage health.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Data { get; init; }

    /// <summary>
    /// Creates a healthy result.
    /// </summary>
    public static StorageHealthResult Healthy(string? message = null, IReadOnlyDictionary<string, object>? data = null)
        => new() { IsHealthy = true, Message = message, Data = data };

    /// <summary>
    /// Creates an unhealthy result.
    /// </summary>
    public static StorageHealthResult Unhealthy(string message, IReadOnlyDictionary<string, object>? data = null)
        => new() { IsHealthy = false, Message = message, Data = data };
}
