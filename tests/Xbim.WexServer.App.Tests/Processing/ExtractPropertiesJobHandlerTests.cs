using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xbim.WexServer.Abstractions.Processing;
using Xbim.WexServer.Abstractions.Storage;
using Xbim.WexServer.App.Processing;
using Xbim.WexServer.Domain.Entities;
using Xbim.WexServer.Domain.Enums;
using Xbim.WexServer.Persistence.EfCore;

namespace Xbim.WexServer.App.Tests.Processing;

public class ExtractPropertiesJobHandlerTests : IDisposable
{
    private readonly string _testDbName;
    private readonly ServiceProvider _serviceProvider;
    private readonly XbimDbContext _dbContext;
    private readonly InMemoryStorageProvider _storageProvider;
    private readonly TestProgressNotifier _progressNotifier;
    private readonly ExtractPropertiesJobHandler _handler;

    public ExtractPropertiesJobHandlerTests()
    {
        _testDbName = $"test_{Guid.NewGuid()}";
        _storageProvider = new InMemoryStorageProvider();
        _progressNotifier = new TestProgressNotifier();

        var services = new ServiceCollection();
        services.AddDbContext<XbimDbContext>(options =>
            options.UseInMemoryDatabase(_testDbName));
        services.AddLogging(builder => builder.AddDebug());
        services.AddSingleton<IStorageProvider>(_storageProvider);
        services.AddSingleton<IProgressNotifier>(_progressNotifier);
        services.AddScoped<ExtractPropertiesJobHandler>();

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<XbimDbContext>();
        _handler = _serviceProvider.GetRequiredService<ExtractPropertiesJobHandler>();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _serviceProvider.Dispose();
    }

    private async Task<(Workspace, Project, Model, FileEntity, ModelVersion)> SetupTestDataAsync()
    {
        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "Test Workspace",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Workspaces.Add(workspace);

        var project = new Project
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            Name = "Test Project",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Projects.Add(project);

        var model = new Model
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Test Model",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Models.Add(model);

        // Create a storage key for the IFC file
        var ifcStorageKey = $"{workspace.Id:N}/{project.Id:N}/test.ifc";

        // Store mock IFC content
        await _storageProvider.PutAsync(ifcStorageKey,
            new MemoryStream("mock-ifc-content"u8.ToArray()),
            "application/x-step");

        var ifcFile = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "test.ifc",
            ContentType = "application/x-step",
            SizeBytes = 1024,
            Kind = FileKind.Source,
            Category = FileCategory.Ifc,
            StorageProvider = _storageProvider.ProviderId,
            StorageKey = ifcStorageKey,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Files.Add(ifcFile);

        var modelVersion = new ModelVersion
        {
            Id = Guid.NewGuid(),
            ModelId = model.Id,
            VersionNumber = 1,
            IfcFileId = ifcFile.Id,
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.ModelVersions.Add(modelVersion);

        await _dbContext.SaveChangesAsync();

        return (workspace, project, model, ifcFile, modelVersion);
    }

    [Fact]
    public void JobType_ReturnsCorrectType()
    {
        Assert.Equal("ExtractProperties", _handler.JobType);
    }

    [Fact]
    public async Task HandleAsync_SendsFailureNotification_WhenModelVersionNotFound()
    {
        // Arrange
        var jobId = Guid.NewGuid().ToString();
        var payload = new ExtractPropertiesJobPayload
        {
            ModelVersionId = Guid.NewGuid() // Non-existent
        };

        // Act
        await _handler.HandleAsync(jobId, payload);

        // Assert
        var notifications = _progressNotifier.GetNotifications();
        var lastNotification = notifications.LastOrDefault();
        Assert.NotNull(lastNotification);
        Assert.True(lastNotification.IsComplete);
        Assert.False(lastNotification.IsSuccess);
        Assert.Contains("not found", lastNotification.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_SkipsProcessing_WhenPropertiesFileAlreadyExists()
    {
        // Arrange - Create a model version that already has properties file
        var (workspace, project, model, ifcFile, _) = await SetupTestDataAsync();

        var propertiesFile = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "existing.properties.json",
            ContentType = "application/json",
            SizeBytes = 2048,
            Kind = FileKind.Artifact,
            Category = FileCategory.Properties,
            StorageProvider = _storageProvider.ProviderId,
            StorageKey = $"{workspace.Id:N}/{project.Id:N}/artifacts/properties/existing.json",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Files.Add(propertiesFile);

        var alreadyProcessedVersion = new ModelVersion
        {
            Id = Guid.NewGuid(),
            ModelId = model.Id,
            VersionNumber = 99,
            IfcFileId = ifcFile.Id,
            PropertiesFileId = propertiesFile.Id, // Already has properties
            Status = ProcessingStatus.Ready,
            ProcessedAt = DateTimeOffset.UtcNow.AddHours(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2)
        };
        _dbContext.ModelVersions.Add(alreadyProcessedVersion);
        await _dbContext.SaveChangesAsync();

        var jobId = Guid.NewGuid().ToString();
        var payload = new ExtractPropertiesJobPayload
        {
            ModelVersionId = alreadyProcessedVersion.Id
        };

        _progressNotifier.Clear();

        // Act
        await _handler.HandleAsync(jobId, payload);

        // Assert - Should skip and send success notification (idempotency)
        var notifications = _progressNotifier.GetNotifications();
        var lastNotification = notifications.LastOrDefault();
        Assert.NotNull(lastNotification);
        Assert.True(lastNotification.IsComplete);
        Assert.True(lastNotification.IsSuccess);

        // Verify no duplicate properties file was created
        var propertiesCount = await _dbContext.Files
            .Where(f => f.Category == FileCategory.Properties && f.ProjectId == project.Id)
            .CountAsync();
        Assert.Equal(1, propertiesCount); // Only the original one
    }

    [Fact]
    public async Task HandleAsync_SendsFailureNotification_WhenIfcFileNotInStorage()
    {
        // Arrange - Create a version with an IFC file that doesn't exist in storage
        var (workspace, project, model, _, _) = await SetupTestDataAsync();

        var missingIfcFile = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "missing.ifc",
            ContentType = "application/x-step",
            SizeBytes = 1024,
            Kind = FileKind.Source,
            Category = FileCategory.Ifc,
            StorageProvider = _storageProvider.ProviderId,
            StorageKey = "nonexistent/path/file.ifc",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Files.Add(missingIfcFile);

        var modelVersion = new ModelVersion
        {
            Id = Guid.NewGuid(),
            ModelId = model.Id,
            VersionNumber = 2,
            IfcFileId = missingIfcFile.Id,
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.ModelVersions.Add(modelVersion);
        await _dbContext.SaveChangesAsync();

        var jobId = Guid.NewGuid().ToString();
        var payload = new ExtractPropertiesJobPayload
        {
            ModelVersionId = modelVersion.Id
        };

        // Act
        await _handler.HandleAsync(jobId, payload);

        // Assert
        var notifications = _progressNotifier.GetNotifications();
        var lastNotification = notifications.LastOrDefault();
        Assert.NotNull(lastNotification);
        Assert.True(lastNotification.IsComplete);
        Assert.False(lastNotification.IsSuccess);
    }

    [Fact]
    public void JobTypeConstant_MatchesExpectedValue()
    {
        // Verify the job type constant is properly defined
        Assert.Equal("ExtractProperties", ExtractPropertiesJobHandler.JobTypeName);
    }

    [Fact]
    public async Task HandleAsync_SendsFailureNotification_WhenIfcFileHasEmptyStorageKey()
    {
        // Arrange - Create a version with an IFC file that has empty storage key
        var (workspace, project, model, _, _) = await SetupTestDataAsync();

        var ifcFileNoStorage = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "nostorage.ifc",
            ContentType = "application/x-step",
            SizeBytes = 1024,
            Kind = FileKind.Source,
            Category = FileCategory.Ifc,
            StorageProvider = _storageProvider.ProviderId,
            StorageKey = "", // Empty storage key
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Files.Add(ifcFileNoStorage);

        var modelVersion = new ModelVersion
        {
            Id = Guid.NewGuid(),
            ModelId = model.Id,
            VersionNumber = 3,
            IfcFileId = ifcFileNoStorage.Id, // Points to file with empty storage key
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.ModelVersions.Add(modelVersion);
        await _dbContext.SaveChangesAsync();

        var jobId = Guid.NewGuid().ToString();
        var payload = new ExtractPropertiesJobPayload
        {
            ModelVersionId = modelVersion.Id
        };

        // Act
        await _handler.HandleAsync(jobId, payload);

        // Assert
        var notifications = _progressNotifier.GetNotifications();
        var lastNotification = notifications.LastOrDefault();
        Assert.NotNull(lastNotification);
        Assert.True(lastNotification.IsComplete);
        Assert.False(lastNotification.IsSuccess);
        Assert.Contains("IFC file not found", lastNotification.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_SendsProgressNotifications()
    {
        // Arrange
        var (_, _, _, _, modelVersion) = await SetupTestDataAsync();
        var jobId = Guid.NewGuid().ToString();
        var payload = new ExtractPropertiesJobPayload
        {
            ModelVersionId = modelVersion.Id
        };

        // Act
        await _handler.HandleAsync(jobId, payload);

        // Assert
        var notifications = _progressNotifier.GetNotifications();
        Assert.NotEmpty(notifications);

        // All notifications should have the correct job ID and model version ID
        foreach (var notification in notifications)
        {
            Assert.Equal(jobId, notification.JobId);
            Assert.Equal(modelVersion.Id, notification.ModelVersionId);
        }

        // Final notification should be complete
        var finalNotification = notifications.Last();
        Assert.True(finalNotification.IsComplete);
    }

    [Fact]
    public async Task HandleAsync_DoesNotCreateDuplicatePropertiesFile_OnRetry()
    {
        // Arrange - Create a model version that already has properties file (simulating retry)
        var (workspace, project, model, ifcFile, _) = await SetupTestDataAsync();

        var existingPropertiesFile = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "test.properties.json",
            ContentType = "application/json",
            SizeBytes = 500,
            Kind = FileKind.Artifact,
            Category = FileCategory.Properties,
            StorageProvider = _storageProvider.ProviderId,
            StorageKey = $"{workspace.Id:N}/{project.Id:N}/artifacts/properties/existing.json",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Files.Add(existingPropertiesFile);

        var modelVersion = new ModelVersion
        {
            Id = Guid.NewGuid(),
            ModelId = model.Id,
            VersionNumber = 5,
            IfcFileId = ifcFile.Id,
            PropertiesFileId = existingPropertiesFile.Id, // Already has properties
            Status = ProcessingStatus.Ready,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.ModelVersions.Add(modelVersion);
        await _dbContext.SaveChangesAsync();

        var jobId = Guid.NewGuid().ToString();
        var payload = new ExtractPropertiesJobPayload
        {
            ModelVersionId = modelVersion.Id
        };

        _progressNotifier.Clear();
        var initialPropertiesCount = await _dbContext.Files
            .Where(f => f.Category == FileCategory.Properties)
            .CountAsync();

        // Act - Run the job again (retry scenario)
        await _handler.HandleAsync(jobId, payload);

        // Assert - No new properties file should be created
        var finalPropertiesCount = await _dbContext.Files
            .Where(f => f.Category == FileCategory.Properties)
            .CountAsync();

        Assert.Equal(initialPropertiesCount, finalPropertiesCount);

        // Should send success notification (idempotent)
        var notifications = _progressNotifier.GetNotifications();
        var lastNotification = notifications.LastOrDefault();
        Assert.NotNull(lastNotification);
        Assert.True(lastNotification.IsComplete);
        Assert.True(lastNotification.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_RecordsErrorInModelVersion_OnFailure()
    {
        // Arrange - Create a version with an IFC file that doesn't exist in storage
        var (workspace, project, model, _, _) = await SetupTestDataAsync();

        var badIfcFile = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "bad.ifc",
            ContentType = "application/x-step",
            SizeBytes = 100,
            Kind = FileKind.Source,
            Category = FileCategory.Ifc,
            StorageProvider = _storageProvider.ProviderId,
            StorageKey = "does-not-exist/bad.ifc",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Files.Add(badIfcFile);

        var modelVersion = new ModelVersion
        {
            Id = Guid.NewGuid(),
            ModelId = model.Id,
            VersionNumber = 4,
            IfcFileId = badIfcFile.Id,
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.ModelVersions.Add(modelVersion);
        await _dbContext.SaveChangesAsync();

        var jobId = Guid.NewGuid().ToString();
        var payload = new ExtractPropertiesJobPayload
        {
            ModelVersionId = modelVersion.Id
        };

        // Act
        await _handler.HandleAsync(jobId, payload);

        // Assert - Error message should be recorded
        await _dbContext.Entry(modelVersion).ReloadAsync();
        Assert.NotNull(modelVersion.ErrorMessage);
        Assert.Contains("Properties extraction failed", modelVersion.ErrorMessage);
    }

    #region Helper Classes

    private class InMemoryStorageProvider : IStorageProvider
    {
        public ConcurrentDictionary<string, byte[]> Storage { get; } = new();

        public string ProviderId => "InMemory";

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

    private class TestProgressNotifier : IProgressNotifier
    {
        private readonly List<ProcessingProgress> _notifications = new();
        private readonly object _lock = new();

        public Task NotifyProgressAsync(ProcessingProgress progress, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _notifications.Add(progress);
            }
            return Task.CompletedTask;
        }

        public IReadOnlyList<ProcessingProgress> GetNotifications()
        {
            lock (_lock)
            {
                return _notifications.ToList();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _notifications.Clear();
            }
        }
    }

    #endregion
}
