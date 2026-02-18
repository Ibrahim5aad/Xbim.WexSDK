namespace Xbim.WexServer.Storage.AzureBlob;

/// <summary>
/// Configuration options for Azure Blob Storage provider.
/// </summary>
public class AzureBlobStorageOptions
{
    /// <summary>
    /// Azure Storage connection string. Required if not using managed identity.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Azure Storage account name. Used with managed identity or SAS tokens.
    /// </summary>
    public string? AccountName { get; set; }

    /// <summary>
    /// Container name for storing blobs. Each workspace gets a prefix within this container.
    /// Default: "Xbim-files"
    /// </summary>
    public string ContainerName { get; set; } = "Xbim-files";

    /// <summary>
    /// Whether to use Azure Managed Identity for authentication.
    /// If true, AccountName must be set.
    /// </summary>
    public bool UseManagedIdentity { get; set; }

    /// <summary>
    /// Whether to create the container if it doesn't exist.
    /// Default: true
    /// </summary>
    public bool CreateContainerIfNotExists { get; set; } = true;

    /// <summary>
    /// The Azure Blob endpoint URL. If not set, will be constructed from AccountName.
    /// Useful for Azurite emulator: "http://127.0.0.1:10000/devstoreaccount1"
    /// </summary>
    public string? BlobEndpoint { get; set; }
}
