using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Octopus.Server.Abstractions.Processing;
using Octopus.Server.Abstractions.Storage;
using Octopus.Server.App.Storage;
using Octopus.Server.Domain.Entities;
using Octopus.Server.Domain.Enums;
using Octopus.Server.Persistence.EfCore;
using Xbim.Common;
using Xbim.Geometry.Abstractions;
using Xbim.Ifc;
using Xbim.ModelGeometry.Scene;

namespace Octopus.Server.App.Processing;

/// <summary>
/// Job handler for IFC to WexBIM conversion.
/// Reads IFC from storage, converts to WexBIM, stores artifact, and updates ModelVersion.
/// </summary>
public class IfcToWexBimJobHandler : IJobHandler<IfcToWexBimJobPayload>
{
    public const string JobTypeName = "IfcToWexBim";

    private readonly OctopusDbContext _dbContext;
    private readonly IStorageProvider _storageProvider;
    private readonly IProgressNotifier _progressNotifier;
    private readonly ILogger<IfcToWexBimJobHandler> _logger;

    public string JobType => JobTypeName;

    public IfcToWexBimJobHandler(
        OctopusDbContext dbContext,
        IStorageProvider storageProvider,
        IProgressNotifier progressNotifier,
        ILogger<IfcToWexBimJobHandler> logger)
    {
        _dbContext = dbContext;
        _storageProvider = storageProvider;
        _progressNotifier = progressNotifier;
        _logger = logger;
    }

    public async Task HandleAsync(string jobId, IfcToWexBimJobPayload payload, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting IFC to WexBIM conversion job {JobId} for ModelVersion {ModelVersionId}",
            jobId, payload.ModelVersionId);

        // Load the model version with related entities
        var modelVersion = await _dbContext.ModelVersions
            .Include(mv => mv.Model)
            .ThenInclude(m => m!.Project)
            .Include(mv => mv.IfcFile)
            .FirstOrDefaultAsync(mv => mv.Id == payload.ModelVersionId, cancellationToken);

        if (modelVersion is null)
        {
            _logger.LogError("ModelVersion {ModelVersionId} not found", payload.ModelVersionId);
            await NotifyFailureAsync(jobId, payload.ModelVersionId, "ModelVersion not found", cancellationToken);
            return;
        }

        // Idempotency check: if already processed successfully, skip
        if (modelVersion.Status == ProcessingStatus.Ready && modelVersion.WexBimFileId.HasValue)
        {
            _logger.LogInformation("ModelVersion {ModelVersionId} already processed (idempotency), skipping",
                payload.ModelVersionId);
            await NotifySuccessAsync(jobId, payload.ModelVersionId, cancellationToken);
            return;
        }

        // Validate current state - only process if Pending or previously Failed
        if (modelVersion.Status != ProcessingStatus.Pending && modelVersion.Status != ProcessingStatus.Failed)
        {
            _logger.LogWarning("ModelVersion {ModelVersionId} is in status {Status}, expected Pending or Failed",
                payload.ModelVersionId, modelVersion.Status);
            return;
        }

        var project = modelVersion.Model?.Project;
        if (project is null)
        {
            _logger.LogError("Project not found for ModelVersion {ModelVersionId}", payload.ModelVersionId);
            await SetFailedStatusAsync(modelVersion, "Project not found", cancellationToken);
            await NotifyFailureAsync(jobId, payload.ModelVersionId, "Project not found", cancellationToken);
            return;
        }

        var ifcFile = modelVersion.IfcFile;
        if (ifcFile is null || string.IsNullOrEmpty(ifcFile.StorageKey))
        {
            _logger.LogError("IFC file not found or has no storage key for ModelVersion {ModelVersionId}",
                payload.ModelVersionId);
            await SetFailedStatusAsync(modelVersion, "IFC file not found", cancellationToken);
            await NotifyFailureAsync(jobId, payload.ModelVersionId, "IFC file not found", cancellationToken);
            return;
        }

        try
        {
            // Transition to Processing status
            modelVersion.Status = ProcessingStatus.Processing;
            await _dbContext.SaveChangesAsync(cancellationToken);

            await NotifyProgressAsync(jobId, payload.ModelVersionId, "Starting", 0, "Starting conversion...", cancellationToken);

            // Download IFC file from storage
            await NotifyProgressAsync(jobId, payload.ModelVersionId, "Downloading", 10, "Downloading IFC file...", cancellationToken);

            using var ifcStream = await _storageProvider.OpenReadAsync(ifcFile.StorageKey, cancellationToken);
            if (ifcStream is null)
            {
                throw new InvalidOperationException($"Failed to open IFC file from storage: {ifcFile.StorageKey}");
            }

            // Write to temp file (xBIM requires file path for large models)
            var tempPath = Path.Combine(Path.GetTempPath(), $"xbim_{Guid.NewGuid()}.ifc");
            try
            {
                await using (var tempFile = File.Create(tempPath))
                {
                    await ifcStream.CopyToAsync(tempFile, cancellationToken);
                }

                await NotifyProgressAsync(jobId, payload.ModelVersionId, "Opening", 20, "Opening IFC model...", cancellationToken);

                // Open IFC file (xBIM geometry services configured at startup in Program.cs)
                using var model = IfcStore.Open(tempPath);
                if (model is null)
                {
                    throw new InvalidOperationException("Failed to open IFC model");
                }

                await NotifyProgressAsync(jobId, payload.ModelVersionId, "Processing", 40, "Processing geometry...", cancellationToken);

                // Generate WexBIM
                var wexbimData = await GenerateWexBimAsync(model, jobId, payload.ModelVersionId, cancellationToken);

                if (wexbimData is null || wexbimData.Length == 0)
                {
                    throw new InvalidOperationException("Failed to generate WexBIM geometry");
                }

                await NotifyProgressAsync(jobId, payload.ModelVersionId, "Storing", 80, "Storing WexBIM artifact...", cancellationToken);

                // Store WexBIM artifact
                var wexbimStorageKey = StorageKeyHelper.GenerateArtifactKey(
                    project.WorkspaceId,
                    project.Id,
                    "wexbim",
                    ".wexbim");

                using var wexbimStream = new MemoryStream(wexbimData);
                await _storageProvider.PutAsync(wexbimStorageKey, wexbimStream, "application/octet-stream", cancellationToken);

                await NotifyProgressAsync(jobId, payload.ModelVersionId, "Finalizing", 90, "Creating artifact record...", cancellationToken);

                // Create WexBIM file record
                var wexbimFile = new FileEntity
                {
                    Id = Guid.NewGuid(),
                    ProjectId = project.Id,
                    Name = $"{Path.GetFileNameWithoutExtension(ifcFile.Name)}.wexbim",
                    ContentType = "application/octet-stream",
                    SizeBytes = wexbimData.Length,
                    Kind = FileKind.Artifact,
                    Category = FileCategory.WexBim,
                    StorageProvider = _storageProvider.ProviderId,
                    StorageKey = wexbimStorageKey,
                    IsDeleted = false,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                _dbContext.Files.Add(wexbimFile);

                // Create lineage link (WexBIM DerivedFrom IFC)
                var fileLink = new FileLink
                {
                    Id = Guid.NewGuid(),
                    SourceFileId = ifcFile.Id,
                    TargetFileId = wexbimFile.Id,
                    LinkType = FileLinkType.DerivedFrom,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                _dbContext.FileLinks.Add(fileLink);

                // Update ModelVersion with artifact reference and Ready status
                modelVersion.WexBimFileId = wexbimFile.Id;
                modelVersion.Status = ProcessingStatus.Ready;
                modelVersion.ProcessedAt = DateTimeOffset.UtcNow;
                modelVersion.ErrorMessage = null;

                await _dbContext.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Successfully converted IFC to WexBIM for ModelVersion {ModelVersionId}. WexBIM size: {Size} bytes",
                    payload.ModelVersionId, wexbimData.Length);

                await NotifySuccessAsync(jobId, payload.ModelVersionId, cancellationToken);
            }
            finally
            {
                // Cleanup temp file
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { /* ignore */ }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing IFC to WexBIM for ModelVersion {ModelVersionId}",
                payload.ModelVersionId);

            await SetFailedStatusAsync(modelVersion, ex.Message, cancellationToken);
            await NotifyFailureAsync(jobId, payload.ModelVersionId, ex.Message, cancellationToken);
        }
    }

    private async Task<byte[]?> GenerateWexBimAsync(
        IModel model,
        string jobId,
        Guid modelVersionId,
        CancellationToken cancellationToken)
    {
        return await Task.Run(async () =>
        {
            try
            {
                await NotifyProgressAsync(jobId, modelVersionId, "Geometry", 50, "Creating geometry context...", CancellationToken.None);

                var context = new Xbim3DModelContext(model, engineVersion: XGeometryEngineVersion.V6);
                
                context.CreateContext(null, true, generateBREPs: true);

                if (cancellationToken.IsCancellationRequested)
                    return null;

                await NotifyProgressAsync(jobId, modelVersionId, "Tessellation", 70, "Generating WexBIM...", CancellationToken.None);
                
                using var memoryStream = new MemoryStream();
                using var wexBimBinaryWriter = new BinaryWriter(memoryStream);

                model.SaveAsWexBim(wexBimBinaryWriter);

                return memoryStream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating WexBIM geometry");
                return null;
            }
        }, cancellationToken);
    }

    private async Task SetFailedStatusAsync(ModelVersion modelVersion, string errorMessage, CancellationToken cancellationToken)
    {
        modelVersion.Status = ProcessingStatus.Failed;
        modelVersion.ErrorMessage = errorMessage;
        modelVersion.ProcessedAt = DateTimeOffset.UtcNow;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save Failed status for ModelVersion {ModelVersionId}", modelVersion.Id);
        }
    }

    private async Task NotifyProgressAsync(string jobId, Guid modelVersionId, string stage, int percentComplete, string message, CancellationToken cancellationToken)
    {
        try
        {
            await _progressNotifier.NotifyProgressAsync(new ProcessingProgress
            {
                JobId = jobId,
                ModelVersionId = modelVersionId,
                Stage = stage,
                PercentComplete = percentComplete,
                Message = message,
                IsComplete = false,
                IsSuccess = false
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send progress notification");
        }
    }

    private async Task NotifySuccessAsync(string jobId, Guid modelVersionId, CancellationToken cancellationToken)
    {
        try
        {
            await _progressNotifier.NotifyProgressAsync(new ProcessingProgress
            {
                JobId = jobId,
                ModelVersionId = modelVersionId,
                Stage = "Complete",
                PercentComplete = 100,
                Message = "Conversion completed successfully",
                IsComplete = true,
                IsSuccess = true
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send success notification");
        }
    }

    private async Task NotifyFailureAsync(string jobId, Guid modelVersionId, string errorMessage, CancellationToken cancellationToken)
    {
        try
        {
            await _progressNotifier.NotifyProgressAsync(new ProcessingProgress
            {
                JobId = jobId,
                ModelVersionId = modelVersionId,
                Stage = "Failed",
                PercentComplete = 0,
                Message = "Conversion failed",
                IsComplete = true,
                IsSuccess = false,
                ErrorMessage = errorMessage
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send failure notification");
        }
    }
}
