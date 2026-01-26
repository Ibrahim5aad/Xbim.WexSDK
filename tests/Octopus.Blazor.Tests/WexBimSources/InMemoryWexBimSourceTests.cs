using Octopus.Blazor.Services.Abstractions;
using Octopus.Blazor.Services.WexBimSources;

namespace Octopus.Blazor.Tests.WexBimSources;

public class InMemoryWexBimSourceTests
{
    [Fact]
    public void Constructor_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        var data = new byte[] { 0x01, 0x02, 0x03 };
        var name = "Test Model";

        // Act
        var source = new InMemoryWexBimSource(data, name);

        // Assert
        Assert.Equal(name, source.Name);
        Assert.Equal(WexBimSourceType.InMemory, source.SourceType);
        Assert.True(source.IsAvailable);
        Assert.False(source.SupportsDirectUrl);
        Assert.Equal(3, source.SizeBytes);
    }

    [Fact]
    public void Constructor_WithCustomId_ShouldUseProvidedId()
    {
        // Arrange
        var data = new byte[] { 0x01 };
        var customId = "custom-id-123";

        // Act
        var source = new InMemoryWexBimSource(data, "Test", customId);

        // Assert
        Assert.Equal(customId, source.Id);
    }

    [Fact]
    public async Task GetDataAsync_ShouldReturnData()
    {
        // Arrange
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var source = new InMemoryWexBimSource(data, "Test");

        // Act
        var result = await source.GetDataAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(data, result);
    }

    [Fact]
    public async Task GetUrlAsync_ShouldReturnNull()
    {
        // Arrange
        var source = new InMemoryWexBimSource(new byte[] { 0x01 }, "Test");

        // Act
        var result = await source.GetUrlAsync();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void UpdateData_ShouldReplaceData()
    {
        // Arrange
        var initialData = new byte[] { 0x01 };
        var newData = new byte[] { 0x02, 0x03, 0x04 };
        var source = new InMemoryWexBimSource(initialData, "Test");

        // Act
        source.UpdateData(newData);

        // Assert
        Assert.Equal(3, source.SizeBytes);
    }

    [Fact]
    public async Task Clear_ShouldMakeSourceUnavailable()
    {
        // Arrange
        var source = new InMemoryWexBimSource(new byte[] { 0x01 }, "Test");
        Assert.True(source.IsAvailable);

        // Act
        source.Clear();

        // Assert
        Assert.False(source.IsAvailable);
        Assert.Equal(0, source.SizeBytes);
        var result = await source.GetDataAsync();
        Assert.Null(result);
    }

    [Fact]
    public void Constructor_WithNullData_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new InMemoryWexBimSource(null!, "Test"));
    }

    [Fact]
    public void Constructor_WithNullName_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new InMemoryWexBimSource(new byte[] { 0x01 }, null!));
    }
}
