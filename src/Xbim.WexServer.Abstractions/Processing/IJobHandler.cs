namespace Xbim.WexServer.Abstractions.Processing;

/// <summary>
/// Marker interface for job handlers to support DI registration discovery.
/// </summary>
public interface IJobHandler
{
    /// <summary>
    /// The job type this handler processes (must match JobEnvelope.Type).
    /// </summary>
    string JobType { get; }
}

/// <summary>
/// Interface for job handlers that process specific job payloads.
/// </summary>
/// <typeparam name="TPayload">The type of the job payload.</typeparam>
public interface IJobHandler<TPayload> : IJobHandler
    where TPayload : class
{
    /// <summary>
    /// Handles the job with the specified payload.
    /// </summary>
    /// <param name="jobId">The unique job ID (for idempotency tracking).</param>
    /// <param name="payload">The deserialized job payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task HandleAsync(string jobId, TPayload payload, CancellationToken cancellationToken = default);
}
