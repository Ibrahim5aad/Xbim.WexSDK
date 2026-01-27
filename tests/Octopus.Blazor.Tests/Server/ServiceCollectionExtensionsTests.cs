using Microsoft.Extensions.DependencyInjection;
using Octopus.Blazor.Services.Abstractions.Server;
using Octopus.Blazor.Services.Server;
using Octopus.Api.Client;

namespace Octopus.Blazor.Tests.Server;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddOctopusBlazorPlatformConnected_ShouldRegisterServerServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOctopusBlazorPlatformConnected("https://localhost:5000");

        // Assert - All server-backed services should be registered
        Assert.Contains(services, d => d.ServiceType == typeof(IWorkspacesService));
        Assert.Contains(services, d => d.ServiceType == typeof(IProjectsService));
        Assert.Contains(services, d => d.ServiceType == typeof(IFilesService));
        Assert.Contains(services, d => d.ServiceType == typeof(IModelsService));
        Assert.Contains(services, d => d.ServiceType == typeof(IUsageService));
        Assert.Contains(services, d => d.ServiceType == typeof(IProcessingService));
    }

    [Fact]
    public void AddOctopusBlazorPlatformConnected_ShouldRegisterApiClient()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOctopusBlazorPlatformConnected("https://localhost:5000");

        // Assert - API client should be registered
        Assert.Contains(services, d => d.ServiceType == typeof(IOctopusApiClient));
    }

    [Fact]
    public void AddOctopusBlazorPlatformConnected_WithEmptyBaseUrl_ShouldThrow()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => services.AddOctopusBlazorPlatformConnected(""));
        Assert.Throws<ArgumentException>(() => services.AddOctopusBlazorPlatformConnected((string)null!));
    }

    [Fact]
    public void AddOctopusBlazorPlatformConnected_WithTokenProvider_ShouldRegisterProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        var tokenProvider = new StaticTokenProvider("test-token");

        // Act
        services.AddOctopusBlazorPlatformConnected("https://localhost:5000", tokenProvider);

        // Assert - Token provider should be registered
        Assert.Contains(services, d => d.ServiceType == typeof(IAuthTokenProvider));
    }

    [Fact]
    public void AddOctopusBlazorPlatformConnected_WithTokenFactory_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();
        Func<Task<string?>> tokenFactory = () => Task.FromResult<string?>("test-token");

        // Act
        services.AddOctopusBlazorPlatformConnected("https://localhost:5000", tokenFactory);

        // Assert
        Assert.Contains(services, d => d.ServiceType == typeof(IWorkspacesService));
    }

    [Fact]
    public void AddOctopusBlazorPlatformConnected_ServicesShouldBeSingletons()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddOctopusBlazorPlatformConnected("https://localhost:5000");

        // Assert - Services should be registered as singletons
        var workspacesDescriptor = services.First(d => d.ServiceType == typeof(IWorkspacesService));
        Assert.Equal(ServiceLifetime.Singleton, workspacesDescriptor.Lifetime);

        var projectsDescriptor = services.First(d => d.ServiceType == typeof(IProjectsService));
        Assert.Equal(ServiceLifetime.Singleton, projectsDescriptor.Lifetime);
    }

    [Fact]
    public void AddOctopusBlazorPlatformConnected_ShouldAlsoRegisterStandaloneServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOctopusBlazorPlatformConnected("https://localhost:5000");

        // Assert - Standalone services should also be available
        Assert.Contains(services, d => d.ServiceType == typeof(Octopus.Blazor.Services.ThemeService));
        Assert.Contains(services, d => d.ServiceType == typeof(Octopus.Blazor.Services.Abstractions.IPropertyService));
    }

    [Fact]
    public void AddOctopusBlazorPlatformConnected_WithBlazorOptions_ShouldConfigureOptions()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOctopusBlazorPlatformConnected("https://localhost:5000", opt =>
        {
            opt.InitialTheme = Octopus.Blazor.Models.ViewerTheme.Dark;
        });
        var provider = services.BuildServiceProvider();
        var options = provider.GetService<Octopus.Blazor.OctopusBlazorOptions>();

        // Assert
        Assert.NotNull(options);
        Assert.Equal(Octopus.Blazor.Models.ViewerTheme.Dark, options.InitialTheme);
    }

    [Fact]
    public void AddOctopusBlazorPlatformConnected_WithClientOptions_ShouldNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert - Should not throw
        services.AddOctopusBlazorPlatformConnected(
            "https://localhost:5000",
            blazorOpt => { },
            clientOpt => { clientOpt.TokenFactory = _ => Task.FromResult<string?>("token"); });
    }

    [Fact]
    public void ServerServices_ShouldImplementCorrectInterfaces()
    {
        // Assert
        Assert.True(typeof(IWorkspacesService).IsAssignableFrom(typeof(WorkspacesService)));
        Assert.True(typeof(IProjectsService).IsAssignableFrom(typeof(ProjectsService)));
        Assert.True(typeof(IFilesService).IsAssignableFrom(typeof(FilesService)));
        Assert.True(typeof(IModelsService).IsAssignableFrom(typeof(ModelsService)));
        Assert.True(typeof(IUsageService).IsAssignableFrom(typeof(UsageService)));
        Assert.True(typeof(IProcessingService).IsAssignableFrom(typeof(ProcessingService)));
    }
}
