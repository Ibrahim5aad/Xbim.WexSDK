using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octopus.Server.Abstractions.Storage;

namespace Octopus.Server.Storage.AzureBlob;

/// <summary>
/// Azure Blob Storage implementation of IStorageProvider.
/// Provides per-workspace isolation through blob name prefixes.
/// </summary>
public class AzureBlobStorageProvider : IStorageProvider
{
    private readonly BlobContainerClient _containerClient;
    private readonly ILogger<AzureBlobStorageProvider> _logger;
    private readonly AzureBlobStorageOptions _options;
    private bool _containerCreated;

    public AzureBlobStorageProvider(
        IOptions<AzureBlobStorageOptions> options,
        ILogger<AzureBlobStorageProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
        _containerClient = CreateContainerClient();
    }

    public string ProviderId => "AzureBlob";

    public bool SupportsDirectUpload => !string.IsNullOrEmpty(_options.ConnectionString);

    private BlobContainerClient CreateContainerClient()
    {
        if (!string.IsNullOrEmpty(_options.ConnectionString))
        {
            var serviceClient = new BlobServiceClient(_options.ConnectionString);
            return serviceClient.GetBlobContainerClient(_options.ContainerName);
        }

        if (_options.UseManagedIdentity && !string.IsNullOrEmpty(_options.AccountName))
        {
            var endpoint = _options.BlobEndpoint ?? $"https://{_options.AccountName}.blob.core.windows.net";
            var serviceClient = new BlobServiceClient(new Uri(endpoint), new DefaultAzureCredential());
            return serviceClient.GetBlobContainerClient(_options.ContainerName);
        }

        if (!string.IsNullOrEmpty(_options.BlobEndpoint))
        {
            // For Azurite emulator or custom endpoints with connection string format
            var serviceClient = new BlobServiceClient(_options.BlobEndpoint);
            return serviceClient.GetBlobContainerClient(_options.ContainerName);
        }

        throw new InvalidOperationException(
            "Azure Blob Storage configuration is invalid. " +
            "Provide either ConnectionString, or set UseManagedIdentity=true with AccountName.");
    }

    private async Task EnsureContainerExistsAsync(CancellationToken cancellationToken)
    {
        if (_containerCreated || !_options.CreateContainerIfNotExists)
            return;

        try
        {
            await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            _containerCreated = true;
            _logger.LogDebug("Ensured container {ContainerName} exists", _options.ContainerName);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // Container already exists, ignore
            _containerCreated = true;
        }
    }

    /// <inheritdoc />
    public async Task<string> PutAsync(
        string key,
        Stream content,
        string? contentType = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureContainerExistsAsync(cancellationToken);

        var blobClient = _containerClient.GetBlobClient(key);

        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = contentType ?? "application/octet-stream"
            },
            // Use If-None-Match: * to prevent overwrites (concurrency safety)
            Conditions = new BlobRequestConditions
            {
                IfNoneMatch = ETag.All
            }
        };

        try
        {
            await blobClient.UploadAsync(content, uploadOptions, cancellationToken);
            _logger.LogDebug("Uploaded blob {BlobName} with content type {ContentType}", key, contentType);
            return key;
        }
        catch (RequestFailedException ex) when (ex.Status == 409 || ex.ErrorCode == "BlobAlreadyExists")
        {
            // Blob already exists - this is a concurrency conflict
            _logger.LogWarning("Blob {BlobName} already exists, upload rejected for concurrency safety", key);
            throw new InvalidOperationException($"Blob '{key}' already exists. Use a unique key to avoid conflicts.", ex);
        }
    }

    /// <inheritdoc />
    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        await EnsureContainerExistsAsync(cancellationToken);

        var blobClient = _containerClient.GetBlobClient(key);

        try
        {
            // Stream download for large files - does not load entire blob into memory
            var response = await blobClient.OpenReadAsync(
                new BlobOpenReadOptions(allowModifications: false),
                cancellationToken);

            _logger.LogDebug("Opened read stream for blob {BlobName}", key);
            return response;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug("Blob {BlobName} not found", key);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        await EnsureContainerExistsAsync(cancellationToken);

        var blobClient = _containerClient.GetBlobClient(key);

        try
        {
            var response = await blobClient.DeleteIfExistsAsync(
                DeleteSnapshotsOption.IncludeSnapshots,
                cancellationToken: cancellationToken);

            _logger.LogDebug("Deleted blob {BlobName}: {Deleted}", key, response.Value);
            return response.Value;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to delete blob {BlobName}", key);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        await EnsureContainerExistsAsync(cancellationToken);

        var blobClient = _containerClient.GetBlobClient(key);

        try
        {
            var response = await blobClient.ExistsAsync(cancellationToken);
            return response.Value;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to check existence of blob {BlobName}", key);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken = default)
    {
        await EnsureContainerExistsAsync(cancellationToken);

        var blobClient = _containerClient.GetBlobClient(key);

        try
        {
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            return properties.Value.ContentLength;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string?> GenerateUploadSasUrlAsync(
        string key,
        string? contentType,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        // SAS URL generation requires connection string authentication
        // Managed identity does not support user delegation SAS without additional setup
        if (string.IsNullOrEmpty(_options.ConnectionString))
        {
            _logger.LogDebug("SAS URL generation not supported without connection string");
            return null;
        }

        await EnsureContainerExistsAsync(cancellationToken);

        var blobClient = _containerClient.GetBlobClient(key);

        // Check if the blob client can generate SAS
        if (!blobClient.CanGenerateSasUri)
        {
            _logger.LogWarning("Blob client cannot generate SAS URI for {BlobName}", key);
            return null;
        }

        // Build SAS with write-only permissions
        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = _options.ContainerName,
            BlobName = key,
            Resource = "b", // blob resource
            ExpiresOn = expiresAt
        };

        // Set permissions: Create and Write only (no read, delete, or list)
        sasBuilder.SetPermissions(BlobSasPermissions.Create | BlobSasPermissions.Write);

        // Set content type header if provided
        if (!string.IsNullOrEmpty(contentType))
        {
            sasBuilder.ContentType = contentType;
        }

        var sasUri = blobClient.GenerateSasUri(sasBuilder);
        _logger.LogDebug("Generated SAS URI for blob {BlobName}, expires at {ExpiresAt}", key, expiresAt);

        return sasUri.ToString();
    }
}
