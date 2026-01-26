using Octopus.Blazor.Services.Abstractions;
using Octopus.Blazor.Services.WexBimSources;

namespace Octopus.Blazor.Tests.WexBimSources;

public class StaticAssetWexBimSourceTests
{
    [Fact]
    public void Constructor_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        var relativePath = "models/sample.wexbim";
        var httpClient = new HttpClient { BaseAddress = new Uri("https://localhost:5000/") };

        // Act
        var source = new StaticAssetWexBimSource(relativePath, httpClient);

        // Assert
        Assert.Equal("sample.wexbim", source.Name);
        Assert.Equal("models/sample.wexbim", source.RelativePath);
        Assert.Equal(WexBimSourceType.Url, source.SourceType);
        Assert.True(source.IsAvailable);
        Assert.True(source.SupportsDirectUrl);
    }

    [Fact]
    public void Constructor_WithCustomName_ShouldUseProvidedName()
    {
        // Arrange
        var httpClient = new HttpClient { BaseAddress = new Uri("https://localhost:5000/") };

        // Act
        var source = new StaticAssetWexBimSource("models/sample.wexbim", httpClient, "My Model");

        // Assert
        Assert.Equal("My Model", source.Name);
    }

    [Fact]
    public async Task GetUrlAsync_ShouldReturnFullUrl()
    {
        // Arrange
        var httpClient = new HttpClient { BaseAddress = new Uri("https://localhost:5000/") };
        var source = new StaticAssetWexBimSource("models/sample.wexbim", httpClient);

        // Act
        var result = await source.GetUrlAsync();

        // Assert
        Assert.Equal("https://localhost:5000/models/sample.wexbim", result);
    }

    [Fact]
    public void FullUrl_WithBaseAddress_ShouldCombineCorrectly()
    {
        // Arrange
        var httpClient = new HttpClient { BaseAddress = new Uri("https://example.com/app/") };
        var source = new StaticAssetWexBimSource("models/sample.wexbim", httpClient);

        // Act
        var fullUrl = source.FullUrl;

        // Assert
        Assert.Equal("https://example.com/app/models/sample.wexbim", fullUrl);
    }

    [Fact]
    public void FullUrl_WithoutBaseAddress_ShouldReturnRelativePath()
    {
        // Arrange
        var httpClient = new HttpClient();
        var source = new StaticAssetWexBimSource("models/sample.wexbim", httpClient);

        // Act
        var fullUrl = source.FullUrl;

        // Assert
        Assert.Equal("/models/sample.wexbim", fullUrl);
    }

    [Fact]
    public void Constructor_WithLeadingSlash_ShouldTrimSlash()
    {
        // Arrange
        var httpClient = new HttpClient { BaseAddress = new Uri("https://localhost/") };

        // Act
        var source = new StaticAssetWexBimSource("/models/sample.wexbim", httpClient);

        // Assert
        Assert.Equal("models/sample.wexbim", source.RelativePath);
    }

    [Fact]
    public void Constructor_WithNullRelativePath_ShouldThrow()
    {
        // Arrange
        var httpClient = new HttpClient();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new StaticAssetWexBimSource(null!, httpClient));
    }

    [Fact]
    public void Constructor_WithNullHttpClient_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new StaticAssetWexBimSource("models/sample.wexbim", null!));
    }
}
