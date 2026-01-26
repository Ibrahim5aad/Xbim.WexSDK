using Microsoft.Extensions.DependencyInjection;
using Octopus.Blazor.Models;
using Octopus.Blazor.Services;
using Octopus.Blazor.Services.Abstractions;
using Octopus.Blazor.Services.Server.Guards;
using Octopus.Blazor.Services.WexBimSources;

namespace Octopus.Blazor.Tests;

/// <summary>
/// Tests for <see cref="ServiceCollectionExtensions"/> standalone and backward-compatible registration methods.
/// </summary>
public class StandaloneServiceCollectionExtensionsTests
{
    #region AddOctopusBlazorStandalone Tests

    [Fact]
    public void AddOctopusBlazorStandalone_ShouldRegisterCoreServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOctopusBlazorStandalone();

        // Assert - Core services should be registered
        Assert.Contains(services, d => d.ServiceType == typeof(ThemeService));
        Assert.Contains(services, d => d.ServiceType == typeof(IPropertyService));
        Assert.Contains(services, d => d.ServiceType == typeof(PropertyService));
        Assert.Contains(services, d => d.ServiceType == typeof(IWexBimSourceProvider));
        Assert.Contains(services, d => d.ServiceType == typeof(WexBimSourceProvider));
        Assert.Contains(services, d => d.ServiceType == typeof(IfcHierarchyService));
    }

    [Fact]
    public void AddOctopusBlazorStandalone_ServicesShouldBeSingletons()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOctopusBlazorStandalone();

        // Assert - Services should be registered as singletons
        var themeDescriptor = services.First(d => d.ServiceType == typeof(ThemeService));
        Assert.Equal(ServiceLifetime.Singleton, themeDescriptor.Lifetime);

        var propertyDescriptor = services.First(d => d.ServiceType == typeof(PropertyService));
        Assert.Equal(ServiceLifetime.Singleton, propertyDescriptor.Lifetime);

        var sourceProviderDescriptor = services.First(d => d.ServiceType == typeof(WexBimSourceProvider));
        Assert.Equal(ServiceLifetime.Singleton, sourceProviderDescriptor.Lifetime);
    }

    [Fact]
    public void AddOctopusBlazorStandalone_WithOptions_ShouldConfigureTheme()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOctopusBlazorStandalone(options =>
        {
            options.InitialTheme = ViewerTheme.Dark;
            options.LightAccentColor = "#custom-light";
            options.DarkAccentColor = "#custom-dark";
        });
        var provider = services.BuildServiceProvider();
        var themeService = provider.GetRequiredService<ThemeService>();

        // Assert
        Assert.Equal(ViewerTheme.Dark, themeService.CurrentTheme);
    }

    [Fact]
    public void AddOctopusBlazorStandalone_WithOptions_ShouldStoreOptions()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOctopusBlazorStandalone(options =>
        {
            options.InitialTheme = ViewerTheme.Light;
        });
        var provider = services.BuildServiceProvider();
        var storedOptions = provider.GetService<OctopusBlazorOptions>();

        // Assert
        Assert.NotNull(storedOptions);
        Assert.Equal(ViewerTheme.Light, storedOptions.InitialTheme);
    }

    [Fact]
    public void AddOctopusBlazorStandalone_WithStandaloneSources_ShouldRegisterInitializer()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOctopusBlazorStandalone(options =>
        {
            options.StandaloneSources = new StandaloneSourceOptions()
                .AddStaticAsset("models/test.wexbim", "Test Model");
        });

        // Assert - Source initializer should be registered
        Assert.Contains(services, d => d.ServiceType == typeof(IWexBimSourceInitializer));
    }

    [Fact]
    public void AddOctopusBlazorStandalone_ServicesCanBeResolved()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddOctopusBlazorStandalone();
        var provider = services.BuildServiceProvider();

        // Act & Assert - Services should be resolvable
        Assert.NotNull(provider.GetRequiredService<ThemeService>());
        Assert.NotNull(provider.GetRequiredService<IPropertyService>());
        Assert.NotNull(provider.GetRequiredService<PropertyService>());
        Assert.NotNull(provider.GetRequiredService<IWexBimSourceProvider>());
        Assert.NotNull(provider.GetRequiredService<WexBimSourceProvider>());
        Assert.NotNull(provider.GetRequiredService<IfcHierarchyService>());
    }

    [Fact]
    public void AddOctopusBlazorStandalone_InterfaceAndConcreteResolveSameInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddOctopusBlazorStandalone();
        var provider = services.BuildServiceProvider();

        // Act
        var propertyInterface = provider.GetRequiredService<IPropertyService>();
        var propertyConcrete = provider.GetRequiredService<PropertyService>();

        var sourceInterface = provider.GetRequiredService<IWexBimSourceProvider>();
        var sourceConcrete = provider.GetRequiredService<WexBimSourceProvider>();

        // Assert - Same instance should be returned
        Assert.Same(propertyInterface, propertyConcrete);
        Assert.Same(sourceInterface, sourceConcrete);
    }

    [Fact]
    public void AddOctopusBlazorStandalone_RegistersGuardServicesInsteadOfRealServerServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOctopusBlazorStandalone();
        var provider = services.BuildServiceProvider();

        // Assert - Server service interfaces ARE registered, but with guard implementations
        // that throw ServerServiceNotConfiguredException when used
        Assert.Contains(services, d => d.ServiceType == typeof(Services.Abstractions.Server.IWorkspacesService));
        Assert.Contains(services, d => d.ServiceType == typeof(Services.Abstractions.Server.IProjectsService));
        Assert.Contains(services, d => d.ServiceType == typeof(Services.Abstractions.Server.IFilesService));
        Assert.Contains(services, d => d.ServiceType == typeof(Services.Abstractions.Server.IModelsService));

        // Verify they are guard implementations (not real services)
        Assert.IsType<Services.Server.Guards.NotConfiguredWorkspacesService>(
            provider.GetRequiredService<Services.Abstractions.Server.IWorkspacesService>());
        Assert.IsType<Services.Server.Guards.NotConfiguredProjectsService>(
            provider.GetRequiredService<Services.Abstractions.Server.IProjectsService>());
    }

    #endregion

    #region AddOctopusBlazor Backward Compatibility Tests

    [Fact]
    public void AddOctopusBlazor_ShouldBeAliasForStandalone()
    {
        // Arrange
        var standaloneServices = new ServiceCollection();
        var backwardCompatServices = new ServiceCollection();

        // Act
        standaloneServices.AddOctopusBlazorStandalone();
        backwardCompatServices.AddOctopusBlazor();

        // Assert - Both should register the same service types
        var standaloneTypes = standaloneServices.Select(d => d.ServiceType).OrderBy(t => t.FullName).ToList();
        var backwardTypes = backwardCompatServices.Select(d => d.ServiceType).OrderBy(t => t.FullName).ToList();

        Assert.Equal(standaloneTypes.Count, backwardTypes.Count);
        for (int i = 0; i < standaloneTypes.Count; i++)
        {
            Assert.Equal(standaloneTypes[i], backwardTypes[i]);
        }
    }

    [Fact]
    public void AddOctopusBlazor_WithOptions_ShouldWorkLikeStandalone()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOctopusBlazor(options =>
        {
            options.InitialTheme = ViewerTheme.Dark;
        });
        var provider = services.BuildServiceProvider();
        var options = provider.GetService<OctopusBlazorOptions>();

        // Assert
        Assert.NotNull(options);
        Assert.Equal(ViewerTheme.Dark, options.InitialTheme);
    }

    [Fact]
    public void AddOctopusBlazor_ServicesCanBeResolved()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddOctopusBlazor();
        var provider = services.BuildServiceProvider();

        // Act & Assert - Services should be resolvable (backward compatibility)
        Assert.NotNull(provider.GetRequiredService<ThemeService>());
        Assert.NotNull(provider.GetRequiredService<IPropertyService>());
        Assert.NotNull(provider.GetRequiredService<IWexBimSourceProvider>());
    }

    #endregion

    #region AddOctopusBlazorServer Tests

    [Fact]
    public void AddOctopusBlazorServer_ShouldIncludeStandaloneServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOctopusBlazorServer();

        // Assert - Standalone services should be included
        Assert.Contains(services, d => d.ServiceType == typeof(ThemeService));
        Assert.Contains(services, d => d.ServiceType == typeof(IPropertyService));
        Assert.Contains(services, d => d.ServiceType == typeof(IWexBimSourceProvider));
    }

    [Fact]
    public void AddOctopusBlazorServer_ShouldRegisterIfcModelService()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOctopusBlazorServer();

        // Assert - IFC model service should be registered
        Assert.Contains(services, d => d.ServiceType == typeof(IIfcModelService));
        Assert.Contains(services, d => d.ServiceType == typeof(IfcModelService));
    }

    [Fact]
    public void AddOctopusBlazorServer_WithOptions_ShouldConfigureTheme()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOctopusBlazorServer(options =>
        {
            options.InitialTheme = ViewerTheme.Light;
        });
        var provider = services.BuildServiceProvider();
        var themeService = provider.GetRequiredService<ThemeService>();

        // Assert
        Assert.Equal(ViewerTheme.Light, themeService.CurrentTheme);
    }

    [Fact]
    public void AddOctopusBlazorServer_RegistersGuardServicesNotRealServerConnectedServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOctopusBlazorServer();
        var provider = services.BuildServiceProvider();

        // Assert - Server service interfaces ARE registered with guard implementations
        // (AddOctopusBlazorServer is for IFC processing only, not API connectivity)
        Assert.Contains(services, d => d.ServiceType == typeof(Services.Abstractions.Server.IWorkspacesService));
        Assert.Contains(services, d => d.ServiceType == typeof(Services.Abstractions.Server.IProjectsService));

        // Verify they are guard implementations (not real services)
        Assert.IsType<Services.Server.Guards.NotConfiguredWorkspacesService>(
            provider.GetRequiredService<Services.Abstractions.Server.IWorkspacesService>());
        Assert.IsType<Services.Server.Guards.NotConfiguredProjectsService>(
            provider.GetRequiredService<Services.Abstractions.Server.IProjectsService>());
    }

    #endregion

    #region ConfigureStandaloneHttpClient Tests

    [Fact]
    public void ConfigureStandaloneHttpClient_WithoutSources_ShouldRegisterNoOpInitializer()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddOctopusBlazorStandalone(); // No sources configured

        // Act
        services.ConfigureStandaloneHttpClient(sp => new HttpClient());

        // Assert - Initializer should be registered
        Assert.Contains(services, d => d.ServiceType == typeof(IWexBimSourceInitializer));
    }

    [Fact]
    public void ConfigureStandaloneHttpClient_WithSources_ShouldRegisterHttpClientInitializer()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddOctopusBlazorStandalone(options =>
        {
            options.StandaloneSources = new StandaloneSourceOptions()
                .AddStaticAsset("test.wexbim", "Test");
        });

        // Act
        services.ConfigureStandaloneHttpClient(sp => new HttpClient());

        // Assert - HttpClient initializer should be registered
        Assert.Contains(services, d => d.ServiceType == typeof(IWexBimSourceInitializer));
    }

    #endregion

    #region Double Registration Idempotency Tests

    [Fact]
    public void AddOctopusBlazorStandalone_CalledTwice_ShouldNotDuplicateServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOctopusBlazorStandalone();
        services.AddOctopusBlazorStandalone();

        // Assert - Services should not be duplicated (TryAdd semantics)
        var themeCount = services.Count(d => d.ServiceType == typeof(ThemeService));
        var propertyServiceCount = services.Count(d => d.ServiceType == typeof(PropertyService));

        Assert.Equal(1, themeCount);
        Assert.Equal(1, propertyServiceCount);
    }

    [Fact]
    public void AddOctopusBlazor_ThenAddOctopusBlazorStandalone_ShouldNotDuplicate()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddOctopusBlazor();
        services.AddOctopusBlazorStandalone();

        // Assert - Services should not be duplicated
        var themeCount = services.Count(d => d.ServiceType == typeof(ThemeService));
        Assert.Equal(1, themeCount);
    }

    #endregion
}
