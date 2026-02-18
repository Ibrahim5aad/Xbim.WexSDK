using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xbim.WexServer.Abstractions.Processing;

namespace Xbim.WexServer.Processing.Tests;

public class ProcessingWorkerServiceTests
{
    // Test payload type
    public record TestJobPayload(string ModelVersionId, int Priority);

    // Test handler that tracks invocations
    public class TestJobHandler : IJobHandler<TestJobPayload>
    {
        public string JobType => "TestJob";
        public List<(string JobId, TestJobPayload Payload)> Invocations { get; } = new();
        public int SideEffectCounter { get; set; }

        public Task HandleAsync(string jobId, TestJobPayload payload, CancellationToken cancellationToken = default)
        {
            Invocations.Add((jobId, payload));
            SideEffectCounter++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Enqueue_WorkerDispatch_HandlerInvoked()
    {
        // Arrange
        var services = new ServiceCollection();
        var queue = new ChannelQueue();
        var tracker = new InMemoryProcessedJobTracker();
        var registry = new JobHandlerRegistry();
        var handler = new TestJobHandler();

        // Register the handler type
        registry.Register<TestJobPayload, TestJobHandler>("TestJob");

        services.AddSingleton<IProcessingQueue>(queue);
        services.AddSingleton<IProcessedJobTracker>(tracker);
        services.AddSingleton(registry);
        services.AddSingleton<TestJobHandler>(handler);
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();

        // Create worker service
        var worker = new ProcessingWorkerService(
            queue,
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            tracker,
            registry,
            Array.Empty<JobHandlerRegistration>(),
            NullLogger<ProcessingWorkerService>.Instance);

        using var cts = new CancellationTokenSource();

        // Start worker in background
        var workerTask = worker.StartAsync(cts.Token);

        // Act - enqueue a job
        var payload = new TestJobPayload("model-123", Priority: 1);
        var payloadJson = JsonSerializer.Serialize(payload);
        var envelope = new JobEnvelope
        {
            JobId = "job-001",
            Type = "TestJob",
            PayloadJson = payloadJson,
            CreatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };
        await queue.EnqueueAsync(envelope);

        // Wait for processing
        await Task.Delay(200);

        // Stop worker
        cts.Cancel();
        queue.Complete();
        await workerTask;

        // Assert
        Assert.Single(handler.Invocations);
        Assert.Equal("job-001", handler.Invocations[0].JobId);
        Assert.Equal("model-123", handler.Invocations[0].Payload.ModelVersionId);
        Assert.Equal(1, handler.SideEffectCounter);
    }

    [Fact]
    public async Task DuplicateJobId_DoesNotExecuteHandlerTwice()
    {
        // Arrange
        var services = new ServiceCollection();
        var queue = new ChannelQueue();
        var tracker = new InMemoryProcessedJobTracker();
        var registry = new JobHandlerRegistry();
        var handler = new TestJobHandler();

        // Register the handler type
        registry.Register<TestJobPayload, TestJobHandler>("TestJob");

        services.AddSingleton<IProcessingQueue>(queue);
        services.AddSingleton<IProcessedJobTracker>(tracker);
        services.AddSingleton(registry);
        services.AddSingleton<TestJobHandler>(handler);
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();

        // Create worker service
        var worker = new ProcessingWorkerService(
            queue,
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            tracker,
            registry,
            Array.Empty<JobHandlerRegistration>(),
            NullLogger<ProcessingWorkerService>.Instance);

        using var cts = new CancellationTokenSource();

        // Start worker in background
        var workerTask = worker.StartAsync(cts.Token);

        // Act - enqueue the SAME job ID twice (simulating at-least-once delivery)
        var payload = new TestJobPayload("model-456", Priority: 2);
        var payloadJson = JsonSerializer.Serialize(payload);

        var envelope1 = new JobEnvelope
        {
            JobId = "duplicate-job-id",
            Type = "TestJob",
            PayloadJson = payloadJson,
            CreatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        var envelope2 = new JobEnvelope
        {
            JobId = "duplicate-job-id", // Same ID
            Type = "TestJob",
            PayloadJson = payloadJson,
            CreatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        await queue.EnqueueAsync(envelope1);
        await queue.EnqueueAsync(envelope2);

        // Wait for processing
        await Task.Delay(300);

        // Stop worker
        cts.Cancel();
        queue.Complete();
        await workerTask;

        // Assert - handler should only be invoked once despite duplicate delivery
        Assert.Single(handler.Invocations);
        Assert.Equal(1, handler.SideEffectCounter);
        Assert.Equal("duplicate-job-id", handler.Invocations[0].JobId);
    }

    [Fact]
    public async Task MultipleJobTypes_DispatchToCorrectHandlers()
    {
        // Arrange
        var services = new ServiceCollection();
        var queue = new ChannelQueue();
        var tracker = new InMemoryProcessedJobTracker();
        var registry = new JobHandlerRegistry();
        var handler1 = new TestJobHandler();
        var handler2 = new AnotherJobHandler();

        // Register different handlers for different job types
        registry.Register<TestJobPayload, TestJobHandler>("TestJob");
        registry.Register<AnotherPayload, AnotherJobHandler>("AnotherJob");

        services.AddSingleton<IProcessingQueue>(queue);
        services.AddSingleton<IProcessedJobTracker>(tracker);
        services.AddSingleton(registry);
        services.AddSingleton<TestJobHandler>(handler1);
        services.AddSingleton<AnotherJobHandler>(handler2);
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();

        var worker = new ProcessingWorkerService(
            queue,
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            tracker,
            registry,
            Array.Empty<JobHandlerRegistration>(),
            NullLogger<ProcessingWorkerService>.Instance);

        using var cts = new CancellationTokenSource();
        var workerTask = worker.StartAsync(cts.Token);

        // Act
        var envelope1 = new JobEnvelope
        {
            JobId = "job-type-1",
            Type = "TestJob",
            PayloadJson = JsonSerializer.Serialize(new TestJobPayload("m1", 1)),
            CreatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        var envelope2 = new JobEnvelope
        {
            JobId = "job-type-2",
            Type = "AnotherJob",
            PayloadJson = JsonSerializer.Serialize(new AnotherPayload("data")),
            CreatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        await queue.EnqueueAsync(envelope1);
        await queue.EnqueueAsync(envelope2);

        await Task.Delay(300);
        cts.Cancel();
        queue.Complete();
        await workerTask;

        // Assert
        Assert.Single(handler1.Invocations);
        Assert.Single(handler2.Invocations);
    }

    [Fact]
    public async Task FailedJob_CanBeRetried()
    {
        // Arrange
        var services = new ServiceCollection();
        var queue = new ChannelQueue();
        var tracker = new InMemoryProcessedJobTracker();
        var registry = new JobHandlerRegistry();
        var handler = new FailingThenSucceedingHandler();

        registry.Register<TestJobPayload, FailingThenSucceedingHandler>("TestJob");

        services.AddSingleton<IProcessingQueue>(queue);
        services.AddSingleton<IProcessedJobTracker>(tracker);
        services.AddSingleton(registry);
        services.AddSingleton<FailingThenSucceedingHandler>(handler);
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();

        var worker = new ProcessingWorkerService(
            queue,
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            tracker,
            registry,
            Array.Empty<JobHandlerRegistration>(),
            NullLogger<ProcessingWorkerService>.Instance);

        using var cts = new CancellationTokenSource();
        var workerTask = worker.StartAsync(cts.Token);

        // Act - First attempt (will fail)
        var envelope1 = new JobEnvelope
        {
            JobId = "retry-job",
            Type = "TestJob",
            PayloadJson = JsonSerializer.Serialize(new TestJobPayload("m1", 1)),
            CreatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };
        await queue.EnqueueAsync(envelope1);

        // Wait for first invocation to complete (with timeout)
        var firstCompleted = await Task.WhenAny(handler.FirstInvocationCompleted, Task.Delay(5000));
        Assert.True(firstCompleted == handler.FirstInvocationCompleted, "First invocation did not complete within timeout");

        // Retry the same job
        var envelope2 = new JobEnvelope
        {
            JobId = "retry-job",
            Type = "TestJob",
            PayloadJson = JsonSerializer.Serialize(new TestJobPayload("m1", 1)),
            CreatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };
        await queue.EnqueueAsync(envelope2);

        // Wait for second invocation to complete (with timeout)
        var secondCompleted = await Task.WhenAny(handler.SecondInvocationCompleted, Task.Delay(5000));
        Assert.True(secondCompleted == handler.SecondInvocationCompleted, "Second invocation did not complete within timeout");

        cts.Cancel();
        queue.Complete();
        await workerTask;

        // Assert - handler was invoked twice (failed first, then retry allowed)
        Assert.Equal(2, handler.InvocationCount);
        Assert.True(await tracker.IsCompletedAsync("retry-job"));
    }

    // Additional test payload and handler types
    public record AnotherPayload(string Data);

    public class AnotherJobHandler : IJobHandler<AnotherPayload>
    {
        public string JobType => "AnotherJob";
        public List<(string JobId, AnotherPayload Payload)> Invocations { get; } = new();

        public Task HandleAsync(string jobId, AnotherPayload payload, CancellationToken cancellationToken = default)
        {
            Invocations.Add((jobId, payload));
            return Task.CompletedTask;
        }
    }

    public class FailingThenSucceedingHandler : IJobHandler<TestJobPayload>
    {
        public string JobType => "TestJob";
        public int InvocationCount { get; private set; }

        private readonly TaskCompletionSource _firstInvocation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondInvocation = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Completes when the first invocation has finished (after throwing).
        /// </summary>
        public Task FirstInvocationCompleted => _firstInvocation.Task;

        /// <summary>
        /// Completes when the second invocation has finished (successfully).
        /// </summary>
        public Task SecondInvocationCompleted => _secondInvocation.Task;

        public Task HandleAsync(string jobId, TestJobPayload payload, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            if (InvocationCount == 1)
            {
                _firstInvocation.TrySetResult();
                throw new InvalidOperationException("Simulated failure on first attempt");
            }
            _secondInvocation.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
