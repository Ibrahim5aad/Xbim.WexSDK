using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Octopus.Server.Abstractions.Processing;

namespace Octopus.Server.Processing;

/// <summary>
/// Hosted worker service that consumes jobs from the queue and dispatches to handlers.
/// </summary>
public sealed class ProcessingWorkerService : BackgroundService
{
    private readonly IProcessingQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IProcessedJobTracker _jobTracker;
    private readonly ILogger<ProcessingWorkerService> _logger;
    private readonly JobHandlerRegistry _handlerRegistry;

    public ProcessingWorkerService(
        IProcessingQueue queue,
        IServiceScopeFactory scopeFactory,
        IProcessedJobTracker jobTracker,
        JobHandlerRegistry handlerRegistry,
        IEnumerable<JobHandlerRegistration> registrations,
        ILogger<ProcessingWorkerService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _jobTracker = jobTracker;
        _handlerRegistry = handlerRegistry;
        _logger = logger;

        // Populate the registry from registered handler registrations
        foreach (var registration in registrations)
        {
            RegisterHandler(registration);
        }
    }

    private void RegisterHandler(JobHandlerRegistration registration)
    {
        // Use the non-generic registration method on the registry
        var method = typeof(JobHandlerRegistry).GetMethod(nameof(JobHandlerRegistry.Register));
        var genericMethod = method?.MakeGenericMethod(registration.PayloadType, registration.HandlerType);
        genericMethod?.Invoke(_handlerRegistry, [registration.JobType]);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Processing worker started with {HandlerCount} registered handlers",
            _handlerRegistry.Count);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var envelope = await _queue.DequeueAsync(stoppingToken);

                if (envelope is null)
                {
                    // Queue closed or cancelled
                    break;
                }

                await ProcessJobAsync(envelope, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in processing worker main loop");
                // Brief delay before retrying to avoid tight error loops
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }

        _logger.LogInformation("Processing worker stopped");
    }

    private async Task ProcessJobAsync(JobEnvelope envelope, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Processing job {JobId} of type {JobType}", envelope.JobId, envelope.Type);

        // Check idempotency - has this job already been processed?
        if (!await _jobTracker.TryMarkAsProcessingAsync(envelope.JobId, cancellationToken))
        {
            _logger.LogInformation("Job {JobId} already processed, skipping (idempotency)", envelope.JobId);
            return;
        }

        try
        {
            // Get the handler registration for this job type
            var registration = _handlerRegistry.GetRegistration(envelope.Type);

            if (registration is null)
            {
                _logger.LogWarning("No handler registered for job type {JobType}", envelope.Type);
                await _jobTracker.MarkAsFailedAsync(envelope.JobId, cancellationToken);
                return;
            }

            // Deserialize the payload
            var payload = JsonSerializer.Deserialize(envelope.PayloadJson, registration.PayloadType);

            if (payload is null)
            {
                _logger.LogWarning("Failed to deserialize payload for job {JobId} of type {JobType}",
                    envelope.JobId, envelope.Type);
                await _jobTracker.MarkAsFailedAsync(envelope.JobId, cancellationToken);
                return;
            }

            // Create a scope for the handler
            await using var scope = _scopeFactory.CreateAsyncScope();

            // Resolve the handler
            var handler = scope.ServiceProvider.GetRequiredService(registration.HandlerType);

            // Invoke the handler using reflection (since we don't know TPayload at compile time)
            var handleMethod = registration.HandlerType.GetMethod("HandleAsync");
            if (handleMethod is null)
            {
                _logger.LogError("Handler {HandlerType} does not have HandleAsync method", registration.HandlerType.Name);
                await _jobTracker.MarkAsFailedAsync(envelope.JobId, cancellationToken);
                return;
            }

            var task = (Task?)handleMethod.Invoke(handler, [envelope.JobId, payload, cancellationToken]);
            if (task is not null)
            {
                await task;
            }

            // Mark as completed
            await _jobTracker.MarkAsCompletedAsync(envelope.JobId, cancellationToken);
            _logger.LogInformation("Job {JobId} completed successfully", envelope.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing job {JobId} of type {JobType}", envelope.JobId, envelope.Type);
            await _jobTracker.MarkAsFailedAsync(envelope.JobId, cancellationToken);
        }
    }
}
