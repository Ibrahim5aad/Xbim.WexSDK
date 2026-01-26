using Microsoft.Extensions.DependencyInjection;
using Octopus.Server.Abstractions.Processing;

namespace Octopus.Server.Processing;

/// <summary>
/// Extension methods for registering processing services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the in-memory processing queue and worker services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure handlers.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddInMemoryProcessing(
        this IServiceCollection services,
        Action<ProcessingBuilder>? configure = null)
    {
        // Register the in-memory queue as singleton (shared between enqueue and worker)
        services.AddSingleton<ChannelQueue>();
        services.AddSingleton<IProcessingQueue>(sp => sp.GetRequiredService<ChannelQueue>());

        // Register the in-memory job tracker as singleton (shared for idempotency)
        services.AddSingleton<InMemoryProcessedJobTracker>();
        services.AddSingleton<IProcessedJobTracker>(sp => sp.GetRequiredService<InMemoryProcessedJobTracker>());

        // Register the handler registry as singleton
        services.AddSingleton<JobHandlerRegistry>();

        // Register the worker service
        services.AddHostedService<ProcessingWorkerService>();

        // Configure handlers
        if (configure is not null)
        {
            var builder = new ProcessingBuilder(services);
            configure(builder);
        }

        return services;
    }

    /// <summary>
    /// Adds processing services with a custom queue implementation.
    /// </summary>
    /// <typeparam name="TQueue">The queue implementation type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure handlers.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddProcessing<TQueue>(
        this IServiceCollection services,
        Action<ProcessingBuilder>? configure = null)
        where TQueue : class, IProcessingQueue
    {
        services.AddSingleton<IProcessingQueue, TQueue>();

        // Default to in-memory job tracker
        services.AddSingleton<InMemoryProcessedJobTracker>();
        services.AddSingleton<IProcessedJobTracker>(sp => sp.GetRequiredService<InMemoryProcessedJobTracker>());

        services.AddSingleton<JobHandlerRegistry>();
        services.AddHostedService<ProcessingWorkerService>();

        if (configure is not null)
        {
            var builder = new ProcessingBuilder(services);
            configure(builder);
        }

        return services;
    }

    /// <summary>
    /// Adds processing services with custom queue and job tracker implementations.
    /// </summary>
    /// <typeparam name="TQueue">The queue implementation type.</typeparam>
    /// <typeparam name="TTracker">The job tracker implementation type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure handlers.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddProcessing<TQueue, TTracker>(
        this IServiceCollection services,
        Action<ProcessingBuilder>? configure = null)
        where TQueue : class, IProcessingQueue
        where TTracker : class, IProcessedJobTracker
    {
        services.AddSingleton<IProcessingQueue, TQueue>();
        services.AddSingleton<IProcessedJobTracker, TTracker>();
        services.AddSingleton<JobHandlerRegistry>();
        services.AddHostedService<ProcessingWorkerService>();

        if (configure is not null)
        {
            var builder = new ProcessingBuilder(services);
            configure(builder);
        }

        return services;
    }
}

/// <summary>
/// Builder for configuring processing job handlers.
/// </summary>
public sealed class ProcessingBuilder
{
    private readonly IServiceCollection _services;

    public ProcessingBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Registers a job handler for a specific job type.
    /// </summary>
    /// <typeparam name="TPayload">The payload type for the job.</typeparam>
    /// <typeparam name="THandler">The handler type.</typeparam>
    /// <param name="jobType">The job type string.</param>
    /// <returns>The builder for chaining.</returns>
    public ProcessingBuilder AddHandler<TPayload, THandler>(string jobType)
        where TPayload : class
        where THandler : class, IJobHandler<TPayload>
    {
        // Register the handler as scoped (new instance per job)
        _services.AddScoped<THandler>();

        // Register a startup action to add to the registry
        _services.AddSingleton(new JobHandlerRegistration(jobType, typeof(TPayload), typeof(THandler)));

        return this;
    }

    /// <summary>
    /// Gets the underlying service collection.
    /// </summary>
    public IServiceCollection Services => _services;
}
