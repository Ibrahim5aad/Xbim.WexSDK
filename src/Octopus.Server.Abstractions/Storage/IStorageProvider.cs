namespace Octopus.Server.Abstractions.Storage;

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
}
