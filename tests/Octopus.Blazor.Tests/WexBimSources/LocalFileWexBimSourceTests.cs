using Octopus.Blazor.Services.Abstractions;
using Octopus.Blazor.Services.WexBimSources;

namespace Octopus.Blazor.Tests.WexBimSources;

public class LocalFileWexBimSourceTests
{
    [Fact]
    public void Constructor_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        var filePath = @"C:\models\sample.wexbim";

        // Act
        var source = new LocalFileWexBimSource(filePath);

        // Assert
        Assert.Equal("sample.wexbim", source.Name);
        Assert.Equal(filePath, source.FilePath);
        Assert.Equal(WexBimSourceType.LocalFile, source.SourceType);
        Assert.False(source.SupportsDirectUrl);
    }

    [Fact]
    public void Constructor_WithCustomName_ShouldUseProvidedName()
    {
        // Arrange
        var filePath = @"C:\models\sample.wexbim";
        var customName = "My Custom Model";

        // Act
        var source = new LocalFileWexBimSource(filePath, customName);

        // Assert
        Assert.Equal(customName, source.Name);
    }

    [Fact]
    public void IsAvailable_WhenFileDoesNotExist_ShouldReturnFalse()
    {
        // Arrange
        var source = new LocalFileWexBimSource(@"C:\nonexistent\file.wexbim");

        // Act & Assert
        Assert.False(source.IsAvailable);
    }

    [Fact]
    public async Task GetDataAsync_WhenFileDoesNotExist_ShouldThrowFileNotFound()
    {
        // Arrange
        var source = new LocalFileWexBimSource(@"C:\nonexistent\file.wexbim");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => source.GetDataAsync());
    }

    [Fact]
    public async Task GetUrlAsync_ShouldReturnNull()
    {
        // Arrange
        var source = new LocalFileWexBimSource(@"C:\models\sample.wexbim");

        // Act
        var result = await source.GetUrlAsync();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Constructor_WithNullFilePath_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new LocalFileWexBimSource(null!));
    }

    [Fact]
    public void GetFileInfo_WhenFileDoesNotExist_ShouldReturnNull()
    {
        // Arrange
        var source = new LocalFileWexBimSource(@"C:\nonexistent\file.wexbim");

        // Act
        var fileInfo = source.GetFileInfo();

        // Assert
        Assert.Null(fileInfo);
    }

    [Fact]
    public async Task GetDataAsync_WithExistingFile_ShouldReturnData()
    {
        // Arrange - create a temporary file
        var tempFile = Path.GetTempFileName();
        var testData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        await File.WriteAllBytesAsync(tempFile, testData);

        try
        {
            var source = new LocalFileWexBimSource(tempFile);

            // Act
            var result = await source.GetDataAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(testData, result);
            Assert.True(source.IsAvailable);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void GetFileInfo_WithExistingFile_ShouldReturnFileInfo()
    {
        // Arrange - create a temporary file
        var tempFile = Path.GetTempFileName();
        File.WriteAllBytes(tempFile, new byte[] { 0x01 });

        try
        {
            var source = new LocalFileWexBimSource(tempFile);

            // Act
            var fileInfo = source.GetFileInfo();

            // Assert
            Assert.NotNull(fileInfo);
            Assert.Equal(tempFile, fileInfo.FullName);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
