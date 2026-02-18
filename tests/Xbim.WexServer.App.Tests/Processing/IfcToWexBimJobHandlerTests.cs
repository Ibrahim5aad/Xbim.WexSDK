using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xbim.WexServer.Abstractions.Processing;
using Xbim.WexServer.Abstractions.Storage;
using Xbim.WexServer.App.Processing;
using Xbim.WexServer.Domain.Entities;
using Xbim.WexServer.Domain.Enums;
using Xbim.WexServer.Persistence.EfCore;

namespace Xbim.WexServer.App.Tests.Processing;

public class IfcToWexBimJobHandlerTests : IDisposable
{
    private readonly string _testDbName;
    private readonly ServiceProvider _serviceProvider;
    private readonly XbimDbContext _dbContext;
    private readonly InMemoryStorageProvider _storageProvider;
    private readonly TestProgressNotifier _progressNotifier;
    private readonly IfcToWexBimJobHandler _handler;

    public IfcToWexBimJobHandlerTests()
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
        services.AddScoped<IfcToWexBimJobHandler>();

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<XbimDbContext>();
        _handler = _serviceProvider.GetRequiredService<IfcToWexBimJobHandler>();
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

        // Store mock IFC content (the handler will try to process this)
        // Since we're testing in isolation without xBIM native libs,
        // we'll need to mock or handle the failure case
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
        Assert.Equal("IfcToWexBim", _handler.JobType);
    }

    [Fact]
    public async Task HandleAsync_TransitionsStatusToFailed_WhenModelVersionNotFound()
    {
        // Arrange
        var jobId = Guid.NewGuid().ToString();
        var payload = new IfcToWexBimJobPayload
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
    public async Task HandleAsync_SetsStatusToProcessing_WhenStarting()
    {
        // Arrange
        var (_, _, _, _, modelVersion) = await SetupTestDataAsync();
        var jobId = Guid.NewGuid().ToString();
        var payload = new IfcToWexBimJobPayload
        {
            ModelVersionId = modelVersion.Id
        };

        // Act - Start processing (will fail due to invalid IFC, but should transition to Processing first)
        await _handler.HandleAsync(jobId, payload);

        // Assert - Check notifications show transition through Processing
        var notifications = _progressNotifier.GetNotifications();
        Assert.Contains(notifications, n => n.Stage == "Starting" || n.Stage == "Downloading");
    }

    [Fact]
    public async Task HandleAsync_SetsStatusToFailed_WhenIfcFileNotFound()
    {
        // Arrange
        var (workspace, project, model, _, _) = await SetupTestDataAsync();

        // Create a model version pointing to non-existent file storage
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
        var payload = new IfcToWexBimJobPayload
        {
            ModelVersionId = modelVersion.Id
        };

        // Act
        await _handler.HandleAsync(jobId, payload);

        // Assert
        await _dbContext.Entry(modelVersion).ReloadAsync();
        Assert.Equal(ProcessingStatus.Failed, modelVersion.Status);
        Assert.NotNull(modelVersion.ErrorMessage);
        Assert.NotNull(modelVersion.ProcessedAt);
    }

    [Fact]
    public async Task HandleAsync_SkipsProcessing_WhenAlreadyReadyWithWexBim()
    {
        // Arrange - Create a model version that's already processed
        var (workspace, project, model, ifcFile, _) = await SetupTestDataAsync();

        var wexbimFile = new FileEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "existing.wexbim",
            ContentType = "application/octet-stream",
            SizeBytes = 2048,
            Kind = FileKind.Artifact,
            Category = FileCategory.WexBim,
            StorageProvider = _storageProvider.ProviderId,
            StorageKey = $"{workspace.Id:N}/{project.Id:N}/artifacts/wexbim/existing.wexbim",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Files.Add(wexbimFile);

        var alreadyProcessedVersion = new ModelVersion
        {
            Id = Guid.NewGuid(),
            ModelId = model.Id,
            VersionNumber = 99,
            IfcFileId = ifcFile.Id,
            WexBimFileId = wexbimFile.Id, // Already has WexBIM
            Status = ProcessingStatus.Ready, // Already processed
            ProcessedAt = DateTimeOffset.UtcNow.AddHours(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2)
        };
        _dbContext.ModelVersions.Add(alreadyProcessedVersion);
        await _dbContext.SaveChangesAsync();

        var jobId = Guid.NewGuid().ToString();
        var payload = new IfcToWexBimJobPayload
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

        // Verify no duplicate WexBIM was created
        var wexbimCount = await _dbContext.Files
            .Where(f => f.Category == FileCategory.WexBim && f.ProjectId == project.Id)
            .CountAsync();
        Assert.Equal(1, wexbimCount); // Only the original one
    }

    [Fact]
    public async Task HandleAsync_SkipsProcessing_WhenStatusIsProcessing()
    {
        // Arrange
        var (_, _, model, ifcFile, _) = await SetupTestDataAsync();

        var processingVersion = new ModelVersion
        {
            Id = Guid.NewGuid(),
            ModelId = model.Id,
            VersionNumber = 10,
            IfcFileId = ifcFile.Id,
            Status = ProcessingStatus.Processing, // Already being processed
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.ModelVersions.Add(processingVersion);
        await _dbContext.SaveChangesAsync();

        var jobId = Guid.NewGuid().ToString();
        var payload = new IfcToWexBimJobPayload
        {
            ModelVersionId = processingVersion.Id
        };

        _progressNotifier.Clear();

        // Act
        await _handler.HandleAsync(jobId, payload);

        // Assert - Should skip (status is not Pending or Failed)
        var notifications = _progressNotifier.GetNotifications();
        Assert.Empty(notifications); // No notifications means it was skipped
    }

    [Fact]
    public async Task HandleAsync_AllowsRetry_WhenStatusIsFailed()
    {
        // Arrange
        var (_, _, model, ifcFile, _) = await SetupTestDataAsync();

        var failedVersion = new ModelVersion
        {
            Id = Guid.NewGuid(),
            ModelId = model.Id,
            VersionNumber = 5,
            IfcFileId = ifcFile.Id,
            Status = ProcessingStatus.Failed, // Previously failed
            ErrorMessage = "Previous error",
            ProcessedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        _dbContext.ModelVersions.Add(failedVersion);
        await _dbContext.SaveChangesAsync();

        var jobId = Guid.NewGuid().ToString();
        var payload = new IfcToWexBimJobPayload
        {
            ModelVersionId = failedVersion.Id
        };

        _progressNotifier.Clear();

        // Act - This will process (and likely fail due to invalid IFC content)
        await _handler.HandleAsync(jobId, payload);

        // Assert - Should attempt processing (will transition to Processing then fail)
        var notifications = _progressNotifier.GetNotifications();
        Assert.NotEmpty(notifications); // Processing was attempted
    }

    [Fact]
    public async Task HandleAsync_RecordsErrorMessage_OnFailure()
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
            VersionNumber = 3,
            IfcFileId = badIfcFile.Id,
            Status = ProcessingStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.ModelVersions.Add(modelVersion);
        await _dbContext.SaveChangesAsync();

        var jobId = Guid.NewGuid().ToString();
        var payload = new IfcToWexBimJobPayload
        {
            ModelVersionId = modelVersion.Id
        };

        // Act
        await _handler.HandleAsync(jobId, payload);

        // Assert
        await _dbContext.Entry(modelVersion).ReloadAsync();
        Assert.Equal(ProcessingStatus.Failed, modelVersion.Status);
        Assert.NotNull(modelVersion.ErrorMessage);
        Assert.NotEmpty(modelVersion.ErrorMessage);
        Assert.NotNull(modelVersion.ProcessedAt);
    }

    [Fact]
    public async Task HandleAsync_SendsProgressNotifications()
    {
        // Arrange
        var (_, _, _, _, modelVersion) = await SetupTestDataAsync();
        var jobId = Guid.NewGuid().ToString();
        var payload = new IfcToWexBimJobPayload
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
