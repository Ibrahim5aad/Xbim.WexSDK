using Octopus.Blazor.Services;
using Octopus.Blazor.Services.Abstractions;
using Octopus.Blazor.Services.WexBimSources;

namespace Octopus.Blazor.Tests.WexBimSources;

public class WexBimSourceProviderTests
{
    [Fact]
    public void RegisterSource_ShouldAddSource()
    {
        // Arrange
        var provider = new WexBimSourceProvider();
        var source = new InMemoryWexBimSource(new byte[] { 0x01 }, "Test");

        // Act
        provider.RegisterSource(source);

        // Assert
        Assert.Single(provider.Sources);
        Assert.Contains(provider.Sources, s => s.Id == source.Id);
    }

    [Fact]
    public void RegisterSource_ShouldRaiseSourcesChangedEvent()
    {
        // Arrange
        var provider = new WexBimSourceProvider();
        var source = new InMemoryWexBimSource(new byte[] { 0x01 }, "Test");
        var eventRaised = false;
        provider.SourcesChanged += (_, _) => eventRaised = true;

        // Act
        provider.RegisterSource(source);

        // Assert
        Assert.True(eventRaised);
    }

    [Fact]
    public void GetSource_ShouldReturnCorrectSource()
    {
        // Arrange
        var provider = new WexBimSourceProvider();
        var source = new InMemoryWexBimSource(new byte[] { 0x01 }, "Test", "test-id");
        provider.RegisterSource(source);

        // Act
        var result = provider.GetSource("test-id");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test-id", result.Id);
    }

    [Fact]
    public void GetSource_WhenNotFound_ShouldReturnNull()
    {
        // Arrange
        var provider = new WexBimSourceProvider();

        // Act
        var result = provider.GetSource("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void UnregisterSource_ShouldRemoveSource()
    {
        // Arrange
        var provider = new WexBimSourceProvider();
        var source = new InMemoryWexBimSource(new byte[] { 0x01 }, "Test", "test-id");
        provider.RegisterSource(source);

        // Act
        var result = provider.UnregisterSource("test-id");

        // Assert
        Assert.True(result);
        Assert.Empty(provider.Sources);
    }

    [Fact]
    public void UnregisterSource_WhenNotFound_ShouldReturnFalse()
    {
        // Arrange
        var provider = new WexBimSourceProvider();

        // Act
        var result = provider.UnregisterSource("nonexistent");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ClearSources_ShouldRemoveAllSources()
    {
        // Arrange
        var provider = new WexBimSourceProvider();
        provider.RegisterSource(new InMemoryWexBimSource(new byte[] { 0x01 }, "Test1"));
        provider.RegisterSource(new InMemoryWexBimSource(new byte[] { 0x02 }, "Test2"));

        // Act
        provider.ClearSources();

        // Assert
        Assert.Empty(provider.Sources);
    }

    [Fact]
    public void GetAvailableSources_ShouldFilterUnavailableSources()
    {
        // Arrange
        var provider = new WexBimSourceProvider();
        var availableSource = new InMemoryWexBimSource(new byte[] { 0x01 }, "Available");
        var unavailableSource = new InMemoryWexBimSource(new byte[] { 0x02 }, "Unavailable");
        unavailableSource.Clear(); // Make it unavailable

        provider.RegisterSource(availableSource);
        provider.RegisterSource(unavailableSource);

        // Act
        var available = provider.GetAvailableSources().ToList();

        // Assert
        Assert.Single(available);
        Assert.Equal("Available", available[0].Name);
    }

    [Fact]
    public void RegisterSource_WithSameId_ShouldReplaceSource()
    {
        // Arrange
        var provider = new WexBimSourceProvider();
        var source1 = new InMemoryWexBimSource(new byte[] { 0x01 }, "Source1", "same-id");
        var source2 = new InMemoryWexBimSource(new byte[] { 0x02 }, "Source2", "same-id");

        // Act
        provider.RegisterSource(source1);
        provider.RegisterSource(source2);

        // Assert
        Assert.Single(provider.Sources);
        Assert.Equal("Source2", provider.GetSource("same-id")?.Name);
    }

    [Fact]
    public void RegisterSource_WithNullSource_ShouldThrow()
    {
        // Arrange
        var provider = new WexBimSourceProvider();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => provider.RegisterSource(null!));
    }

    [Fact]
    public void Sources_ShouldBeReadOnly()
    {
        // Arrange
        var provider = new WexBimSourceProvider();
        provider.RegisterSource(new InMemoryWexBimSource(new byte[] { 0x01 }, "Test"));

        // Act
        var sources = provider.Sources;

        // Assert - Verify it's a read-only collection
        Assert.IsAssignableFrom<IReadOnlyCollection<IWexBimSource>>(sources);
    }
}
