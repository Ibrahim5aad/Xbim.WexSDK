using System.Text.Json;

namespace Octopus.Server.Processing.Tests;

public class ProcessingQueueExtensionsTests
{
    public record SamplePayload(string Id, int Value, string[] Tags);

    [Fact]
    public async Task EnqueueAsync_WithPayload_SerializesCorrectly()
    {
        // Arrange
        var queue = new ChannelQueue();
        var payload = new SamplePayload("test-123", 42, ["tag1", "tag2"]);

        // Act
        var jobId = await queue.EnqueueAsync("SampleJob", payload);

        // Assert
        Assert.NotNull(jobId);
        Assert.Equal(32, jobId.Length); // GUID without hyphens

        var envelope = await queue.DequeueAsync();
        Assert.NotNull(envelope);
        Assert.Equal("SampleJob", envelope.Type);
        Assert.Equal(jobId, envelope.JobId);

        // Verify JSON can be deserialized back
        var deserialized = JsonSerializer.Deserialize<SamplePayload>(envelope.PayloadJson,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.NotNull(deserialized);
        Assert.Equal("test-123", deserialized.Id);
        Assert.Equal(42, deserialized.Value);
        Assert.Equal(2, deserialized.Tags.Length);
    }

    [Fact]
    public async Task EnqueueAsync_WithSpecificJobId_UsesProvidedId()
    {
        // Arrange
        var queue = new ChannelQueue();
        var payload = new SamplePayload("test", 1, []);
        var specificJobId = "my-custom-job-id";

        // Act
        await queue.EnqueueAsync(specificJobId, "SampleJob", payload);

        // Assert
        var envelope = await queue.DequeueAsync();
        Assert.NotNull(envelope);
        Assert.Equal(specificJobId, envelope.JobId);
    }

    [Fact]
    public async Task EnqueueAsync_SetsCreatedAtTimestamp()
    {
        // Arrange
        var queue = new ChannelQueue();
        var payload = new SamplePayload("test", 1, []);
        var beforeEnqueue = DateTimeOffset.UtcNow;

        // Act
        await queue.EnqueueAsync("SampleJob", payload);

        // Assert
        var envelope = await queue.DequeueAsync();
        Assert.NotNull(envelope);
        Assert.True(envelope.CreatedAt >= beforeEnqueue);
        Assert.True(envelope.CreatedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task EnqueueAsync_SetsVersionTo1()
    {
        // Arrange
        var queue = new ChannelQueue();
        var payload = new SamplePayload("test", 1, []);

        // Act
        await queue.EnqueueAsync("SampleJob", payload);

        // Assert
        var envelope = await queue.DequeueAsync();
        Assert.NotNull(envelope);
        Assert.Equal(1, envelope.Version);
    }

    [Fact]
    public async Task EnqueueAsync_WithNullPayload_ThrowsArgumentNullException()
    {
        // Arrange
        var queue = new ChannelQueue();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            queue.EnqueueAsync("SampleJob", (SamplePayload)null!).AsTask());
    }

    [Fact]
    public async Task EnqueueAsync_WithNullJobType_ThrowsArgumentNullException()
    {
        // Arrange
        var queue = new ChannelQueue();
        var payload = new SamplePayload("test", 1, []);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            queue.EnqueueAsync(null!, payload).AsTask());
    }

    [Fact]
    public async Task EnqueueAsync_WithEmptyJobType_ThrowsArgumentException()
    {
        // Arrange
        var queue = new ChannelQueue();
        var payload = new SamplePayload("test", 1, []);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            queue.EnqueueAsync("", payload).AsTask());
    }

    [Fact]
    public async Task PayloadJson_RoundTrips_ComplexObject_WithoutLoss()
    {
        // Arrange
        var queue = new ChannelQueue();
        var payload = new SamplePayload(
            Id: "complex-id",
            Value: int.MaxValue,
            Tags: ["a", "b", "c", "special chars: <>&\"'"]
        );

        // Act
        await queue.EnqueueAsync("SampleJob", payload);
        var envelope = await queue.DequeueAsync();

        // Assert
        Assert.NotNull(envelope);
        var deserialized = JsonSerializer.Deserialize<SamplePayload>(envelope.PayloadJson,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Assert.NotNull(deserialized);
        Assert.Equal(payload.Id, deserialized.Id);
        Assert.Equal(payload.Value, deserialized.Value);
        Assert.Equal(payload.Tags.Length, deserialized.Tags.Length);
        for (int i = 0; i < payload.Tags.Length; i++)
        {
            Assert.Equal(payload.Tags[i], deserialized.Tags[i]);
        }
    }
}
