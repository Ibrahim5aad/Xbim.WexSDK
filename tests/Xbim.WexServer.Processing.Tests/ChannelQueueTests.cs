using Xbim.WexServer.Abstractions.Processing;

namespace Xbim.WexServer.Processing.Tests;

public class ChannelQueueTests
{
    [Fact]
    public async Task EnqueueAsync_AddsJobToQueue()
    {
        // Arrange
        var queue = new ChannelQueue();
        var envelope = CreateEnvelope("test-job-1", "TestJob");

        // Act
        await queue.EnqueueAsync(envelope);

        // Assert
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public async Task DequeueAsync_ReturnsEnqueuedJob()
    {
        // Arrange
        var queue = new ChannelQueue();
        var envelope = CreateEnvelope("test-job-1", "TestJob");
        await queue.EnqueueAsync(envelope);

        // Act
        var result = await queue.DequeueAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test-job-1", result.JobId);
        Assert.Equal("TestJob", result.Type);
    }

    [Fact]
    public async Task DequeueAsync_ReturnsJobsInFifoOrder()
    {
        // Arrange
        var queue = new ChannelQueue();
        var envelope1 = CreateEnvelope("job-1", "TestJob");
        var envelope2 = CreateEnvelope("job-2", "TestJob");
        var envelope3 = CreateEnvelope("job-3", "TestJob");

        await queue.EnqueueAsync(envelope1);
        await queue.EnqueueAsync(envelope2);
        await queue.EnqueueAsync(envelope3);

        // Act & Assert
        var result1 = await queue.DequeueAsync();
        Assert.Equal("job-1", result1?.JobId);

        var result2 = await queue.DequeueAsync();
        Assert.Equal("job-2", result2?.JobId);

        var result3 = await queue.DequeueAsync();
        Assert.Equal("job-3", result3?.JobId);
    }

    [Fact]
    public async Task DequeueAsync_WaitsForNewJob()
    {
        // Arrange
        var queue = new ChannelQueue();
        var envelope = CreateEnvelope("delayed-job", "TestJob");

        // Act
        var dequeueTask = Task.Run(async () => await queue.DequeueAsync());
        await Task.Delay(50); // Give time for dequeue to start waiting

        await queue.EnqueueAsync(envelope);
        var result = await dequeueTask;

        // Assert
        Assert.NotNull(result);
        Assert.Equal("delayed-job", result.JobId);
    }

    [Fact]
    public async Task DequeueAsync_ReturnsNullWhenCancelled()
    {
        // Arrange
        var queue = new ChannelQueue();
        using var cts = new CancellationTokenSource();

        // Act
        var dequeueTask = queue.DequeueAsync(cts.Token);
        await Task.Delay(50); // Give time for dequeue to start waiting
        cts.Cancel();

        var result = await dequeueTask;

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task DequeueAsync_ReturnsNullWhenQueueCompleted()
    {
        // Arrange
        var queue = new ChannelQueue();

        // Act
        var dequeueTask = Task.Run(async () => await queue.DequeueAsync());
        await Task.Delay(50); // Give time for dequeue to start waiting
        queue.Complete();

        var result = await dequeueTask;

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task EnqueueAsync_WithNullEnvelope_ThrowsArgumentNullException()
    {
        // Arrange
        var queue = new ChannelQueue();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            queue.EnqueueAsync(null!).AsTask());
    }

    [Fact]
    public async Task BoundedQueue_WaitsWhenFull()
    {
        // Arrange
        var queue = new ChannelQueue(capacity: 2);
        var envelope1 = CreateEnvelope("job-1", "TestJob");
        var envelope2 = CreateEnvelope("job-2", "TestJob");
        var envelope3 = CreateEnvelope("job-3", "TestJob");

        await queue.EnqueueAsync(envelope1);
        await queue.EnqueueAsync(envelope2);
        // Queue is now full

        // Act - start enqueue that should wait
        var enqueueTask = queue.EnqueueAsync(envelope3).AsTask();
        await Task.Delay(50); // Give time for enqueue to start waiting
        Assert.False(enqueueTask.IsCompleted);

        // Dequeue to make room
        await queue.DequeueAsync();
        await enqueueTask; // Should complete now

        // Assert
        Assert.True(enqueueTask.IsCompleted);
    }

    [Fact]
    public async Task PayloadJson_RoundTrips_WithoutLoss()
    {
        // Arrange
        var queue = new ChannelQueue();
        var originalPayload = """{"modelVersionId":"abc123","jobType":"IfcToWexBim","nested":{"key":"value"}}""";
        var envelope = new JobEnvelope
        {
            JobId = "test-job",
            Type = "TestJob",
            PayloadJson = originalPayload,
            CreatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        // Act
        await queue.EnqueueAsync(envelope);
        var result = await queue.DequeueAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(originalPayload, result.PayloadJson);
    }

    private static JobEnvelope CreateEnvelope(string jobId, string type)
    {
        return new JobEnvelope
        {
            JobId = jobId,
            Type = type,
            PayloadJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            Version = 1
        };
    }
}
