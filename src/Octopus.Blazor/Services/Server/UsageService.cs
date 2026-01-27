using Microsoft.Extensions.Logging;
using Octopus.Blazor.Services.Abstractions.Server;
using Octopus.Api.Client;

namespace Octopus.Blazor.Services.Server;

/// <summary>
/// Server-backed implementation of <see cref="IUsageService"/>.
/// <para>
/// Wraps the generated <see cref="IOctopusApiClient"/> to provide storage usage operations.
/// All API errors are wrapped in <see cref="OctopusServiceException"/> for predictable error handling.
/// </para>
/// </summary>
public class UsageService : IUsageService
{
    private readonly IOctopusApiClient _client;
    private readonly ILogger<UsageService>? _logger;

    /// <summary>
    /// Creates a new UsageService.
    /// </summary>
    /// <param name="client">The Octopus API client.</param>
    /// <param name="logger">Optional logger.</param>
    public UsageService(IOctopusApiClient client, ILogger<UsageService>? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task GetWorkspaceUsageAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogDebug("Getting usage for workspace {WorkspaceId}", workspaceId);
            await _client.GetWorkspaceUsageAsync(workspaceId, cancellationToken);
            _logger?.LogDebug("Retrieved usage for workspace {WorkspaceId}", workspaceId);
        }
        catch (OctopusApiException ex)
        {
            _logger?.LogError(ex, "Failed to get usage for workspace {WorkspaceId}: {StatusCode}", workspaceId, ex.StatusCode);
            throw OctopusServiceException.FromApiException(ex);
        }
    }

    /// <inheritdoc />
    public async Task GetProjectUsageAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogDebug("Getting usage for project {ProjectId}", projectId);
            await _client.GetProjectUsageAsync(projectId, cancellationToken);
            _logger?.LogDebug("Retrieved usage for project {ProjectId}", projectId);
        }
        catch (OctopusApiException ex)
        {
            _logger?.LogError(ex, "Failed to get usage for project {ProjectId}: {StatusCode}", projectId, ex.StatusCode);
            throw OctopusServiceException.FromApiException(ex);
        }
    }
}
