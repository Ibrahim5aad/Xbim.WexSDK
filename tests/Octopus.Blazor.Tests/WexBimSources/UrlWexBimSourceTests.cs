using Octopus.Blazor.Services.Abstractions;
using Octopus.Blazor.Services.WexBimSources;

namespace Octopus.Blazor.Tests.WexBimSources;

public class UrlWexBimSourceTests
{
    [Fact]
    public void Constructor_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        var url = "https://example.com/models/sample.wexbim";

        // Act
        var source = new UrlWexBimSource(url);

        // Assert
        Assert.Equal("sample.wexbim", source.Name);
        Assert.Equal(url, source.Url);
        Assert.Equal(WexBimSourceType.Url, source.SourceType);
        Assert.True(source.IsAvailable);
        Assert.True(source.SupportsDirectUrl);
    }

    [Fact]
    public void Constructor_WithCustomName_ShouldUseProvidedName()
    {
        // Arrange
        var url = "https://example.com/models/sample.wexbim";
        var customName = "My Model";

        // Act
        var source = new UrlWexBimSource(url, customName);

        // Assert
        Assert.Equal(customName, source.Name);
    }

    [Fact]
    public async Task GetUrlAsync_ShouldReturnUrl()
    {
        // Arrange
        var url = "https://example.com/models/sample.wexbim";
        var source = new UrlWexBimSource(url);

        // Act
        var result = await source.GetUrlAsync();

        // Assert
        Assert.Equal(url, result);
    }

    [Fact]
    public async Task GetDataAsync_WithoutHttpClient_ShouldThrowInvalidOperation()
    {
        // Arrange
        var source = new UrlWexBimSource("https://example.com/model.wexbim");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.GetDataAsync());
    }

    [Fact]
    public void Constructor_WithNullUrl_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new UrlWexBimSource(null!));
    }

    [Fact]
    public void Constructor_WithRelativeUrl_ShouldExtractFileName()
    {
        // Arrange
        var url = "models/sample.wexbim";

        // Act
        var source = new UrlWexBimSource(url);

        // Assert
        Assert.Equal("sample.wexbim", source.Name);
    }

    [Fact]
    public void Constructor_WithHttpClientFactory_ShouldNotThrow()
    {
        // Arrange
        var url = "https://example.com/model.wexbim";
        Func<HttpClient> factory = () => new HttpClient();

        // Act
        var source = new UrlWexBimSource(url, factory);

        // Assert
        Assert.Equal(url, source.Url);
    }

    [Fact]
    public void Constructor_WithNullHttpClientFactory_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new UrlWexBimSource("https://example.com", (Func<HttpClient>)null!));
    }

    [Theory]
    [InlineData("https://example.com/path/to/model.wexbim", "model.wexbim")]
    [InlineData("https://example.com/model", "model")]
    [InlineData("https://example.com/", "https://example.com/")]
    [InlineData("simple.wexbim", "simple.wexbim")]
    [InlineData("path/to/file.wexbim", "file.wexbim")]
    public void Constructor_ShouldExtractFileNameFromVariousUrls(string url, string expectedName)
    {
        // Act
        var source = new UrlWexBimSource(url);

        // Assert
        Assert.Equal(expectedName, source.Name);
    }
}
