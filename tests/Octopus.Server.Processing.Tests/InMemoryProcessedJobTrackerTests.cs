namespace Octopus.Server.Processing.Tests;

public class InMemoryProcessedJobTrackerTests
{
    [Fact]
    public async Task TryMarkAsProcessingAsync_ReturnsTrue_ForNewJob()
    {
        // Arrange
        var tracker = new InMemoryProcessedJobTracker();

        // Act
        var result = await tracker.TryMarkAsProcessingAsync("new-job");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task TryMarkAsProcessingAsync_ReturnsFalse_ForDuplicateJob()
    {
        // Arrange
        var tracker = new InMemoryProcessedJobTracker();
        await tracker.TryMarkAsProcessingAsync("existing-job");

        // Act
        var result = await tracker.TryMarkAsProcessingAsync("existing-job");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task TryMarkAsProcessingAsync_ReturnsFalse_ForCompletedJob()
    {
        // Arrange
        var tracker = new InMemoryProcessedJobTracker();
        await tracker.TryMarkAsProcessingAsync("completed-job");
        await tracker.MarkAsCompletedAsync("completed-job");

        // Act
        var result = await tracker.TryMarkAsProcessingAsync("completed-job");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task TryMarkAsProcessingAsync_ReturnsTrue_ForFailedJob_AllowsRetry()
    {
        // Arrange
        var tracker = new InMemoryProcessedJobTracker();
        await tracker.TryMarkAsProcessingAsync("failed-job");
        await tracker.MarkAsFailedAsync("failed-job");

        // Act
        var result = await tracker.TryMarkAsProcessingAsync("failed-job");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task IsCompletedAsync_ReturnsFalse_ForUnknownJob()
    {
        // Arrange
        var tracker = new InMemoryProcessedJobTracker();

        // Act
        var result = await tracker.IsCompletedAsync("unknown-job");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsCompletedAsync_ReturnsTrue_ForCompletedJob()
    {
        // Arrange
        var tracker = new InMemoryProcessedJobTracker();
        await tracker.TryMarkAsProcessingAsync("completed-job");
        await tracker.MarkAsCompletedAsync("completed-job");

        // Act
        var result = await tracker.IsCompletedAsync("completed-job");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task IsCompletedAsync_ReturnsFalse_ForProcessingJob()
    {
        // Arrange
        var tracker = new InMemoryProcessedJobTracker();
        await tracker.TryMarkAsProcessingAsync("processing-job");

        // Act
        var result = await tracker.IsCompletedAsync("processing-job");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsCompletedAsync_ReturnsFalse_ForFailedJob()
    {
        // Arrange
        var tracker = new InMemoryProcessedJobTracker();
        await tracker.TryMarkAsProcessingAsync("failed-job");
        await tracker.MarkAsFailedAsync("failed-job");

        // Act
        var result = await tracker.IsCompletedAsync("failed-job");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task TrackedCount_IncreasesWithJobs()
    {
        // Arrange
        var tracker = new InMemoryProcessedJobTracker();

        // Act
        await tracker.TryMarkAsProcessingAsync("job-1");
        await tracker.TryMarkAsProcessingAsync("job-2");
        await tracker.TryMarkAsProcessingAsync("job-3");

        // Assert
        Assert.Equal(3, tracker.TrackedCount);
    }

    [Fact]
    public async Task Clear_RemovesAllTrackedJobs()
    {
        // Arrange
        var tracker = new InMemoryProcessedJobTracker();
        await tracker.TryMarkAsProcessingAsync("job-1");
        await tracker.TryMarkAsProcessingAsync("job-2");

        // Act
        tracker.Clear();

        // Assert
        Assert.Equal(0, tracker.TrackedCount);
    }

    [Fact]
    public async Task TryMarkAsProcessingAsync_ThrowsOnNullJobId()
    {
        // Arrange
        var tracker = new InMemoryProcessedJobTracker();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            tracker.TryMarkAsProcessingAsync(null!));
    }

    [Fact]
    public async Task TryMarkAsProcessingAsync_ThrowsOnEmptyJobId()
    {
        // Arrange
        var tracker = new InMemoryProcessedJobTracker();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            tracker.TryMarkAsProcessingAsync(""));
    }
}
