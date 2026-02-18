using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xbim.WexServer.Abstractions.Processing;
using Xbim.WexServer.Abstractions.Storage;
using Xbim.WexServer.Contracts;
using Xbim.WexServer.Domain.Entities;
using Xbim.WexServer.Persistence.EfCore;

using WorkspaceRole = Xbim.WexServer.Domain.Enums.WorkspaceRole;
using DomainFileKind = Xbim.WexServer.Domain.Enums.FileKind;
using DomainFileCategory = Xbim.WexServer.Domain.Enums.FileCategory;

namespace Xbim.WexServer.Tests.Endpoints;

public class InMemoryStorageProviderForPropertiesTests : IStorageProvider
{
    public string ProviderId => "InMemory";

    public ConcurrentDictionary<string, byte[]> Storage { get; } = new();

    public Task<string> PutAsync(string key, Stream content, string? contentType = null, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        content.CopyTo(ms);
        Storage[key] = ms.ToArray();
        return Task.FromResult(key);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        if (Storage.TryGetValue(key, out var data))
        {
            return Task.FromResult<Stream?>(new MemoryStream(data));
        }
        return Task.FromResult<Stream?>(null);
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Storage.TryRemove(key, out _));
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Storage.ContainsKey(key));
    }

    public Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken = default)
    {
        if (Storage.TryGetValue(key, out var data))
        {
            return Task.FromResult<long?>(data.Length);
        }
        return Task.FromResult<long?>(null);
    }

    public bool SupportsDirectUpload => false;

    public Task<string?> GenerateUploadSasUrlAsync(string key, string? contentType, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }

    public Task<StorageHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(StorageHealthResult.Healthy("InMemory storage is healthy"));
    }
}

public class PropertiesEndpointsTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _testDbName;
    private readonly InMemoryStorageProviderForPropertiesTests _storageProvider;
    private readonly TestInMemoryProcessingQueue _processingQueue;

    public PropertiesEndpointsTests()
    {
        _testDbName = $"test_{Guid.NewGuid()}";
        _storageProvider = new InMemoryStorageProviderForPropertiesTests();
        _processingQueue = new TestInMemoryProcessingQueue();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureServices(services =>
            {
                // Remove ALL DbContext-related services
                services.RemoveAll(typeof(DbContextOptions<XbimDbContext>));
                services.RemoveAll(typeof(DbContextOptions));
                services.RemoveAll(typeof(XbimDbContext));

                // Remove storage provider and add in-memory one
                services.RemoveAll(typeof(IStorageProvider));
                services.AddSingleton<IStorageProvider>(_storageProvider);

                // Remove processing queue and add in-memory one
                services.RemoveAll(typeof(IProcessingQueue));
                services.AddSingleton<IProcessingQueue>(_processingQueue);

                // Add in-memory database for testing
                services.AddDbContext<XbimDbContext>(options =>
                {
                    options.UseInMemoryDatabase(_testDbName);
                });
            });
        });

        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<WorkspaceDto> CreateWorkspaceAsync(string name = "Test Workspace")
    {
        var response = await _client.PostAsJsonAsync("/api/v1/workspaces",
            new CreateWorkspaceRequest { Name = name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkspaceDto>())!;
    }

    private async Task<ProjectDto> CreateProjectAsync(Guid workspaceId, string name = "Test Project")
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/projects",
            new CreateProjectRequest { Name = name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>())!;
    }

    private async Task<ModelDto> CreateModelAsync(Guid projectId, string name = "Test Model")
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/models",
            new CreateModelRequest { Name = name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ModelDto>())!;
    }

    private async Task<ModelVersionDto> CreateModelVersionAsync(Guid modelId, Guid projectId)
    {
        // Create an IFC file
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        var ifcFile = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = "test.ifc",
            ContentType = "application/x-step",
            SizeBytes = 1024,
            Kind = DomainFileKind.Source,
            Category = DomainFileCategory.Ifc,
            StorageProvider = "InMemory",
            StorageKey = $"test/{Guid.NewGuid()}",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Files.Add(ifcFile);
        await dbContext.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/models/{modelId}/versions",
            new CreateModelVersionRequest { IfcFileId = ifcFile.Id });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ModelVersionDto>())!;
    }

    private async Task CreateSamplePropertiesAsync(Guid modelVersionId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        // Create sample elements with properties
        var element1 = new IfcElement
        {
            Id = Guid.NewGuid(),
            ModelVersionId = modelVersionId,
            EntityLabel = 100,
            GlobalId = "1hOBzLU0P7gfD$aMXCX$cd",
            Name = "Wall-001",
            TypeName = "IfcWall",
            Description = "Exterior Wall",
            ObjectType = "Basic Wall:200mm",
            TypeObjectName = "Basic Wall:200mm",
            TypeObjectType = "IfcWallType",
            ExtractedAt = DateTimeOffset.UtcNow
        };

        var pset1 = new IfcPropertySet
        {
            Id = Guid.NewGuid(),
            ElementId = element1.Id,
            Name = "Pset_WallCommon",
            GlobalId = "2hOBzLU0P7gfD$aMXCX$ce",
            IsTypePropertySet = false
        };

        var prop1 = new IfcProperty
        {
            Id = Guid.NewGuid(),
            PropertySetId = pset1.Id,
            Name = "IsExternal",
            Value = "True",
            ValueType = "boolean"
        };

        var prop2 = new IfcProperty
        {
            Id = Guid.NewGuid(),
            PropertySetId = pset1.Id,
            Name = "ThermalTransmittance",
            Value = "0.25",
            ValueType = "double",
            Unit = "W/(m2.K)"
        };

        pset1.Properties.Add(prop1);
        pset1.Properties.Add(prop2);
        element1.PropertySets.Add(pset1);

        var qset1 = new IfcQuantitySet
        {
            Id = Guid.NewGuid(),
            ElementId = element1.Id,
            Name = "Qto_WallBaseQuantities",
            GlobalId = "3hOBzLU0P7gfD$aMXCX$cf"
        };

        var qty1 = new IfcQuantity
        {
            Id = Guid.NewGuid(),
            QuantitySetId = qset1.Id,
            Name = "NetSideArea",
            Value = 25.5,
            ValueType = "area",
            Unit = "m2"
        };

        qset1.Quantities.Add(qty1);
        element1.QuantitySets.Add(qset1);

        // Second element - a door
        var element2 = new IfcElement
        {
            Id = Guid.NewGuid(),
            ModelVersionId = modelVersionId,
            EntityLabel = 200,
            GlobalId = "4hOBzLU0P7gfD$aMXCX$cg",
            Name = "Door-001",
            TypeName = "IfcDoor",
            Description = "Interior Door",
            ExtractedAt = DateTimeOffset.UtcNow
        };

        var pset2 = new IfcPropertySet
        {
            Id = Guid.NewGuid(),
            ElementId = element2.Id,
            Name = "Pset_DoorCommon",
            IsTypePropertySet = false
        };

        var prop3 = new IfcProperty
        {
            Id = Guid.NewGuid(),
            PropertySetId = pset2.Id,
            Name = "FireRating",
            Value = "30",
            ValueType = "integer"
        };

        pset2.Properties.Add(prop3);
        element2.PropertySets.Add(pset2);

        // Third element - a window
        var element3 = new IfcElement
        {
            Id = Guid.NewGuid(),
            ModelVersionId = modelVersionId,
            EntityLabel = 300,
            GlobalId = "5hOBzLU0P7gfD$aMXCX$ch",
            Name = "Window-001",
            TypeName = "IfcWindow",
            ExtractedAt = DateTimeOffset.UtcNow
        };

        dbContext.IfcElements.AddRange(element1, element2, element3);
        await dbContext.SaveChangesAsync();
    }

    #region Query Properties Tests

    [Fact]
    public async Task QueryProperties_ReturnsPagedResults_WithDefaultPaging()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var version = await CreateModelVersionAsync(model.Id, project.Id);
        await CreateSamplePropertiesAsync(version.Id);

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/modelversions/{version.Id}/properties");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<IfcElementDto>>();
        Assert.NotNull(result);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize); // Default page size
        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public async Task QueryProperties_ReturnsPagedResults_WithCustomPaging()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var version = await CreateModelVersionAsync(model.Id, project.Id);
        await CreateSamplePropertiesAsync(version.Id);

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/modelversions/{version.Id}/properties?page=1&pageSize=2");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<IfcElementDto>>();
        Assert.NotNull(result);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(2, result.Items.Count);
        Assert.True(result.HasNextPage);
    }

    [Fact]
    public async Task QueryProperties_FiltersByEntityLabel()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var version = await CreateModelVersionAsync(model.Id, project.Id);
        await CreateSamplePropertiesAsync(version.Id);

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/modelversions/{version.Id}/properties?entityLabel=100");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<IfcElementDto>>();
        Assert.NotNull(result);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Wall-001", result.Items[0].Name);
        Assert.Equal(100, result.Items[0].EntityLabel);
    }

    [Fact]
    public async Task QueryProperties_FiltersByGlobalId()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var version = await CreateModelVersionAsync(model.Id, project.Id);
        await CreateSamplePropertiesAsync(version.Id);

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/modelversions/{version.Id}/properties?globalId=4hOBzLU0P7gfD$aMXCX$cg");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<IfcElementDto>>();
        Assert.NotNull(result);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Door-001", result.Items[0].Name);
    }

    [Fact]
    public async Task QueryProperties_FiltersByTypeName()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var version = await CreateModelVersionAsync(model.Id, project.Id);
        await CreateSamplePropertiesAsync(version.Id);

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/modelversions/{version.Id}/properties?typeName=IfcWall");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<IfcElementDto>>();
        Assert.NotNull(result);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal("IfcWall", result.Items[0].TypeName);
    }

    [Fact]
    public async Task QueryProperties_FiltersByPropertySetName()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var version = await CreateModelVersionAsync(model.Id, project.Id);
        await CreateSamplePropertiesAsync(version.Id);

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/modelversions/{version.Id}/properties?propertySetName=Pset_DoorCommon");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<IfcElementDto>>();
        Assert.NotNull(result);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Door-001", result.Items[0].Name);
    }

    [Fact]
    public async Task QueryProperties_FiltersByName()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var version = await CreateModelVersionAsync(model.Id, project.Id);
        await CreateSamplePropertiesAsync(version.Id);

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/modelversions/{version.Id}/properties?name=Window");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<IfcElementDto>>();
        Assert.NotNull(result);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Window-001", result.Items[0].Name);
    }

    [Fact]
    public async Task QueryProperties_IncludesPropertiesAndQuantities()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var version = await CreateModelVersionAsync(model.Id, project.Id);
        await CreateSamplePropertiesAsync(version.Id);

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/modelversions/{version.Id}/properties?entityLabel=100");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<IfcElementDto>>();
        Assert.NotNull(result);
        var element = result.Items[0];

        // Check property sets
        Assert.Single(element.PropertySets);
        var pset = element.PropertySets[0];
        Assert.Equal("Pset_WallCommon", pset.Name);
        Assert.Equal(2, pset.Properties.Count);

        // Check individual properties
        var isExternal = pset.Properties.FirstOrDefault(p => p.Name == "IsExternal");
        Assert.NotNull(isExternal);
        Assert.Equal("True", isExternal.Value);
        Assert.Equal("boolean", isExternal.ValueType);

        // Check quantity sets
        Assert.Single(element.QuantitySets);
        var qset = element.QuantitySets[0];
        Assert.Equal("Qto_WallBaseQuantities", qset.Name);
        Assert.Single(qset.Quantities);
        Assert.Equal("NetSideArea", qset.Quantities[0].Name);
        Assert.Equal(25.5, qset.Quantities[0].Value);
    }

    [Fact]
    public async Task QueryProperties_ReturnsEmptyList_WhenNoElements()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var version = await CreateModelVersionAsync(model.Id, project.Id);
        // Don't create any properties

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/modelversions/{version.Id}/properties");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<IfcElementDto>>();
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task QueryProperties_ReturnsNotFound_WhenModelVersionDoesNotExist()
    {
        // Arrange
        var randomId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/modelversions/{randomId}/properties");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task QueryProperties_ReturnsNotFound_WhenUserHasNoAccess()
    {
        // Arrange - Create version in project user doesn't have access to
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "Inaccessible Workspace",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Workspaces.Add(workspace);

        var project = new Project
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            Name = "Hidden Project",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Projects.Add(project);

        var model = new Model
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Hidden Model",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Models.Add(model);

        var ifcFile = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "test.ifc",
            ContentType = "application/x-step",
            SizeBytes = 1024,
            Kind = DomainFileKind.Source,
            Category = DomainFileCategory.Ifc,
            StorageProvider = "InMemory",
            StorageKey = "test/key",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Files.Add(ifcFile);

        var version = new ModelVersion
        {
            Id = Guid.NewGuid(),
            ModelId = model.Id,
            VersionNumber = 1,
            IfcFileId = ifcFile.Id,
            Status = Domain.Enums.ProcessingStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.ModelVersions.Add(version);
        await dbContext.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/modelversions/{version.Id}/properties");

        // Assert - Returns 404 to avoid revealing version existence
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task QueryProperties_LimitsPageSizeTo100()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var version = await CreateModelVersionAsync(model.Id, project.Id);
        await CreateSamplePropertiesAsync(version.Id);

        // Act - Request a page size larger than the maximum
        var response = await _client.GetAsync(
            $"/api/v1/modelversions/{version.Id}/properties?pageSize=500");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedList<IfcElementDto>>();
        Assert.NotNull(result);
        Assert.Equal(100, result.PageSize); // Should be clamped to 100
    }

    #endregion

    #region Get Element Properties Tests

    [Fact]
    public async Task GetElementProperties_ReturnsElement_WhenExists()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var version = await CreateModelVersionAsync(model.Id, project.Id);
        await CreateSamplePropertiesAsync(version.Id);

        // Get the element ID from the database
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();
        var element = await dbContext.IfcElements
            .FirstAsync(e => e.ModelVersionId == version.Id && e.EntityLabel == 100);

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/modelversions/{version.Id}/properties/elements/{element.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IfcElementDto>();
        Assert.NotNull(result);
        Assert.Equal(element.Id, result.Id);
        Assert.Equal("Wall-001", result.Name);
        Assert.Equal(100, result.EntityLabel);
        Assert.Single(result.PropertySets);
        Assert.Single(result.QuantitySets);
    }

    [Fact]
    public async Task GetElementProperties_ReturnsNotFound_WhenElementDoesNotExist()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var version = await CreateModelVersionAsync(model.Id, project.Id);
        var randomElementId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/modelversions/{version.Id}/properties/elements/{randomElementId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetElementProperties_ReturnsNotFound_WhenElementInDifferentModelVersion()
    {
        // Arrange
        var workspace = await CreateWorkspaceAsync();
        var project = await CreateProjectAsync(workspace.Id);
        var model = await CreateModelAsync(project.Id);
        var version1 = await CreateModelVersionAsync(model.Id, project.Id);
        var version2 = await CreateModelVersionAsync(model.Id, project.Id);
        await CreateSamplePropertiesAsync(version1.Id);

        // Get the element ID from version 1
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();
        var element = await dbContext.IfcElements
            .FirstAsync(e => e.ModelVersionId == version1.Id && e.EntityLabel == 100);

        // Act - Try to access element from version 1 using version 2's endpoint
        var response = await _client.GetAsync(
            $"/api/v1/modelversions/{version2.Id}/properties/elements/{element.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetElementProperties_ReturnsNotFound_WhenModelVersionDoesNotExist()
    {
        // Arrange
        var randomVersionId = Guid.NewGuid();
        var randomElementId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/modelversions/{randomVersionId}/properties/elements/{randomElementId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetElementProperties_ReturnsNotFound_WhenUserHasNoAccess()
    {
        // Arrange - Create version in project user doesn't have access to
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();

        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "Inaccessible Workspace",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Workspaces.Add(workspace);

        var project = new Project
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            Name = "Hidden Project",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Projects.Add(project);

        var model = new Model
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Hidden Model",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Models.Add(model);

        var ifcFile = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "test.ifc",
            ContentType = "application/x-step",
            SizeBytes = 1024,
            Kind = DomainFileKind.Source,
            Category = DomainFileCategory.Ifc,
            StorageProvider = "InMemory",
            StorageKey = "test/key",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Files.Add(ifcFile);

        var version = new ModelVersion
        {
            Id = Guid.NewGuid(),
            ModelId = model.Id,
            VersionNumber = 1,
            IfcFileId = ifcFile.Id,
            Status = Domain.Enums.ProcessingStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.ModelVersions.Add(version);

        var element = new IfcElement
        {
            Id = Guid.NewGuid(),
            ModelVersionId = version.Id,
            EntityLabel = 100,
            Name = "Hidden Element",
            TypeName = "IfcWall",
            ExtractedAt = DateTimeOffset.UtcNow
        };
        dbContext.IfcElements.Add(element);
        await dbContext.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync(
            $"/api/v1/modelversions/{version.Id}/properties/elements/{element.Id}");

        // Assert - Returns 404 to avoid revealing version existence
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion
}
