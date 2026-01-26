using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Octopus.Server.Abstractions.Processing;

namespace Octopus.Server.Processing.Tests;

public class ServiceCollectionExtensionsTests
{
    public record TestPayload(string Data);

    public class TestHandler : IJobHandler<TestPayload>
    {
        public string JobType => "TestJob";
        public Task HandleAsync(string jobId, TestPayload payload, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    [Fact]
    public void AddInMemoryProcessing_RegistersRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddInMemoryProcessing();
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetService<IProcessingQueue>());
        Assert.NotNull(provider.GetService<IProcessedJobTracker>());
        Assert.NotNull(provider.GetService<JobHandlerRegistry>());
    }

    [Fact]
    public void AddInMemoryProcessing_RegistersChannelQueueAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInMemoryProcessing();
        var provider = services.BuildServiceProvider();

        // Act
        var queue1 = provider.GetService<IProcessingQueue>();
        var queue2 = provider.GetService<IProcessingQueue>();

        // Assert
        Assert.Same(queue1, queue2);
    }

    [Fact]
    public void AddInMemoryProcessing_RegistersJobTrackerAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInMemoryProcessing();
        var provider = services.BuildServiceProvider();

        // Act
        var tracker1 = provider.GetService<IProcessedJobTracker>();
        var tracker2 = provider.GetService<IProcessedJobTracker>();

        // Assert
        Assert.Same(tracker1, tracker2);
    }

    [Fact]
    public void AddInMemoryProcessing_WithHandler_RegistersHandler()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddInMemoryProcessing(builder =>
        {
            builder.AddHandler<TestPayload, TestHandler>("TestJob");
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var registrations = provider.GetServices<JobHandlerRegistration>().ToList();
        Assert.Single(registrations);
        Assert.Equal("TestJob", registrations[0].JobType);
        Assert.Equal(typeof(TestPayload), registrations[0].PayloadType);
        Assert.Equal(typeof(TestHandler), registrations[0].HandlerType);
    }

    [Fact]
    public void AddInMemoryProcessing_WithMultipleHandlers_RegistersAll()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddInMemoryProcessing(builder =>
        {
            builder.AddHandler<TestPayload, TestHandler>("TestJob");
            builder.AddHandler<AnotherPayload, AnotherHandler>("AnotherJob");
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var registrations = provider.GetServices<JobHandlerRegistration>().ToList();
        Assert.Equal(2, registrations.Count);
    }

    [Fact]
    public void AddInMemoryProcessing_RegistersHostedService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddInMemoryProcessing();

        // Assert
        var hostedServices = services
            .Where(s => s.ServiceType == typeof(IHostedService))
            .ToList();
        Assert.Contains(hostedServices, s => s.ImplementationType == typeof(ProcessingWorkerService));
    }

    [Fact]
    public void ProcessingBuilder_CanChainHandlerRegistrations()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddInMemoryProcessing(builder =>
        {
            builder
                .AddHandler<TestPayload, TestHandler>("TestJob")
                .AddHandler<AnotherPayload, AnotherHandler>("AnotherJob");
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var registrations = provider.GetServices<JobHandlerRegistration>().ToList();
        Assert.Equal(2, registrations.Count);
    }

    [Fact]
    public void AddProcessing_WithCustomQueue_UsesCustomImplementation()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddProcessing<CustomQueue>();
        var provider = services.BuildServiceProvider();

        // Assert
        var queue = provider.GetService<IProcessingQueue>();
        Assert.NotNull(queue);
        Assert.IsType<CustomQueue>(queue);
    }

    [Fact]
    public void AddProcessing_WithCustomQueueAndTracker_UsesBothCustomImplementations()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddProcessing<CustomQueue, CustomTracker>();
        var provider = services.BuildServiceProvider();

        // Assert
        var queue = provider.GetService<IProcessingQueue>();
        var tracker = provider.GetService<IProcessedJobTracker>();
        Assert.IsType<CustomQueue>(queue);
        Assert.IsType<CustomTracker>(tracker);
    }

    // Additional types for testing
    public record AnotherPayload(int Value);

    public class AnotherHandler : IJobHandler<AnotherPayload>
    {
        public string JobType => "AnotherJob";
        public Task HandleAsync(string jobId, AnotherPayload payload, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    public class CustomQueue : IProcessingQueue
    {
        public ValueTask EnqueueAsync(JobEnvelope envelope, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
        public ValueTask<JobEnvelope?> DequeueAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<JobEnvelope?>(null);
    }

    public class CustomTracker : IProcessedJobTracker
    {
        public Task<bool> TryMarkAsProcessingAsync(string jobId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
        public Task MarkAsCompletedAsync(string jobId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task MarkAsFailedAsync(string jobId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task<bool> IsCompletedAsync(string jobId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }
}
