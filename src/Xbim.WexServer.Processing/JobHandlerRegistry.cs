namespace Xbim.WexServer.Processing;

/// <summary>
/// Registration information for a job handler.
/// </summary>
public record JobHandlerRegistration(string JobType, Type PayloadType, Type HandlerType);

/// <summary>
/// Registry of job handlers, mapping job types to their handlers.
/// </summary>
public sealed class JobHandlerRegistry
{
    private readonly Dictionary<string, JobHandlerRegistration> _registrations = new();

    /// <summary>
    /// Registers a handler for a specific job type.
    /// </summary>
    /// <typeparam name="TPayload">The payload type for the job.</typeparam>
    /// <typeparam name="THandler">The handler type.</typeparam>
    /// <param name="jobType">The job type string.</param>
    public void Register<TPayload, THandler>(string jobType)
        where TPayload : class
        where THandler : class
    {
        _registrations[jobType] = new JobHandlerRegistration(jobType, typeof(TPayload), typeof(THandler));
    }

    /// <summary>
    /// Gets the registration for a specific job type.
    /// </summary>
    /// <param name="jobType">The job type string.</param>
    /// <returns>The registration, or null if not found.</returns>
    public JobHandlerRegistration? GetRegistration(string jobType)
    {
        return _registrations.TryGetValue(jobType, out var registration) ? registration : null;
    }

    /// <summary>
    /// Gets all registered job types.
    /// </summary>
    public IEnumerable<string> RegisteredTypes => _registrations.Keys;

    /// <summary>
    /// Gets the count of registered handlers.
    /// </summary>
    public int Count => _registrations.Count;
}
