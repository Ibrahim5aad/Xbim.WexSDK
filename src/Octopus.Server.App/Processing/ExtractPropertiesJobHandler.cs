using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Octopus.Server.Abstractions.Processing;
using Octopus.Server.Abstractions.Storage;
using Octopus.Server.App.Storage;
using Octopus.Server.Domain.Entities;
using Octopus.Server.Domain.Enums;
using Octopus.Server.Persistence.EfCore;
using Xbim.Common;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace Octopus.Server.App.Processing;

/// <summary>
/// Job handler for extracting properties from IFC files.
/// Stores properties as a SQLite artifact file and optionally persists to Octopus database.
/// </summary>
public class ExtractPropertiesJobHandler : IJobHandler<ExtractPropertiesJobPayload>
{
    public const string JobTypeName = "ExtractProperties";

    private readonly OctopusDbContext _dbContext;
    private readonly IStorageProvider _storageProvider;
    private readonly IProgressNotifier _progressNotifier;
    private readonly ILogger<ExtractPropertiesJobHandler> _logger;

    public string JobType => JobTypeName;

    public ExtractPropertiesJobHandler(
        OctopusDbContext dbContext,
        IStorageProvider storageProvider,
        IProgressNotifier progressNotifier,
        ILogger<ExtractPropertiesJobHandler> logger)
    {
        _dbContext = dbContext;
        _storageProvider = storageProvider;
        _progressNotifier = progressNotifier;
        _logger = logger;
    }

    public async Task HandleAsync(string jobId, ExtractPropertiesJobPayload payload, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting properties extraction job {JobId} for ModelVersion {ModelVersionId} (PersistToDatabase: {PersistToDb})",
            jobId, payload.ModelVersionId, payload.PersistToDatabase);

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

        // Idempotency check: if already has properties file, skip
        if (modelVersion.PropertiesFileId.HasValue)
        {
            _logger.LogInformation("ModelVersion {ModelVersionId} already has properties file (idempotency), skipping",
                payload.ModelVersionId);
            await NotifySuccessAsync(jobId, payload.ModelVersionId, cancellationToken);
            return;
        }

        // Validate prerequisites
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
            await NotifyProgressAsync(jobId, payload.ModelVersionId, "Starting", 0, "Starting properties extraction...", cancellationToken);

            // Download IFC file from storage
            await NotifyProgressAsync(jobId, payload.ModelVersionId, "Downloading", 10, "Downloading IFC file...", cancellationToken);

            using var ifcStream = await _storageProvider.OpenReadAsync(ifcFile.StorageKey, cancellationToken);
            if (ifcStream is null)
            {
                throw new InvalidOperationException($"Failed to open IFC file from storage: {ifcFile.StorageKey}");
            }

            // Write to temp file (xBIM requires file path for large models)
            var tempIfcPath = Path.Combine(Path.GetTempPath(), $"xbim_{Guid.NewGuid()}.ifc");
            var tempSqlitePath = Path.Combine(Path.GetTempPath(), $"props_{Guid.NewGuid()}.db");

            try
            {
                await using (var tempFile = File.Create(tempIfcPath))
                {
                    await ifcStream.CopyToAsync(tempFile, cancellationToken);
                }

                await NotifyProgressAsync(jobId, payload.ModelVersionId, "Opening", 20, "Opening IFC model...", cancellationToken);

                // Open IFC file (xBIM geometry services configured at startup in Program.cs)
                using var model = IfcStore.Open(tempIfcPath);
                if (model is null)
                {
                    throw new InvalidOperationException("Failed to open IFC model");
                }

                await NotifyProgressAsync(jobId, payload.ModelVersionId, "Extracting", 30, "Extracting properties to SQLite...", cancellationToken);

                // Extract properties to SQLite file
                var (elementCount, sqliteSize) = await ExtractPropertiesToSqliteAsync(
                    model, tempSqlitePath, payload.ModelVersionId, jobId, cancellationToken);

                if (elementCount == 0)
                {
                    _logger.LogWarning("No elements extracted from IFC model for ModelVersion {ModelVersionId}", payload.ModelVersionId);
                }

                await NotifyProgressAsync(jobId, payload.ModelVersionId, "Storing", 75, "Storing SQLite artifact...", cancellationToken);

                // Store SQLite artifact
                var propertiesStorageKey = StorageKeyHelper.GenerateArtifactKey(
                    project.WorkspaceId,
                    project.Id,
                    "properties",
                    ".db");

                await using (var sqliteFileStream = File.OpenRead(tempSqlitePath))
                {
                    await _storageProvider.PutAsync(propertiesStorageKey, sqliteFileStream, "application/x-sqlite3", cancellationToken);
                }

                await NotifyProgressAsync(jobId, payload.ModelVersionId, "Finalizing", 85, "Creating artifact record...", cancellationToken);

                // Create properties file record
                var propertiesFile = new FileEntity
                {
                    Id = Guid.NewGuid(),
                    ProjectId = project.Id,
                    Name = $"{Path.GetFileNameWithoutExtension(ifcFile.Name)}.properties.db",
                    ContentType = "application/x-sqlite3",
                    SizeBytes = sqliteSize,
                    Kind = FileKind.Artifact,
                    Category = FileCategory.Properties,
                    StorageProvider = _storageProvider.ProviderId,
                    StorageKey = propertiesStorageKey,
                    IsDeleted = false,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                _dbContext.Files.Add(propertiesFile);

                // Create lineage link (Properties derived from IFC)
                var fileLink = new FileLink
                {
                    Id = Guid.NewGuid(),
                    SourceFileId = ifcFile.Id,
                    TargetFileId = propertiesFile.Id,
                    LinkType = FileLinkType.PropertiesOf,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                _dbContext.FileLinks.Add(fileLink);

                // Update ModelVersion with artifact reference
                modelVersion.PropertiesFileId = propertiesFile.Id;

                await _dbContext.SaveChangesAsync(cancellationToken);

                // Optionally persist to Octopus database
                if (payload.PersistToDatabase)
                {
                    await NotifyProgressAsync(jobId, payload.ModelVersionId, "Persisting", 90, "Persisting to database...", cancellationToken);
                    await PersistToOctopusDatabaseAsync(tempSqlitePath, payload.ModelVersionId, cancellationToken);
                }

                _logger.LogInformation(
                    "Successfully extracted properties for ModelVersion {ModelVersionId}. Elements: {ElementCount}, SQLite size: {Size} bytes, PersistToDb: {PersistToDb}",
                    payload.ModelVersionId, elementCount, sqliteSize, payload.PersistToDatabase);

                await NotifySuccessAsync(jobId, payload.ModelVersionId, cancellationToken);
            }
            finally
            {
                // Cleanup temp files
                if (File.Exists(tempIfcPath))
                {
                    try { File.Delete(tempIfcPath); } catch { /* ignore */ }
                }
                if (File.Exists(tempSqlitePath))
                {
                    try { File.Delete(tempSqlitePath); } catch { /* ignore */ }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting properties for ModelVersion {ModelVersionId}",
                payload.ModelVersionId);

            await SetFailedStatusAsync(modelVersion, ex.Message, cancellationToken);
            await NotifyFailureAsync(jobId, payload.ModelVersionId, ex.Message, cancellationToken);
        }
    }

    private async Task<(int elementCount, long fileSize)> ExtractPropertiesToSqliteAsync(
        IModel model,
        string sqlitePath,
        Guid modelVersionId,
        string jobId,
        CancellationToken cancellationToken)
    {
        var connectionString = $"Data Source={sqlitePath}";

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Create schema
        await CreateSqliteSchemaAsync(connection, cancellationToken);

        var products = model.Instances.OfType<IIfcProduct>().ToList();
        var totalProducts = products.Count;
        var processedCount = 0;
        var extractedAt = DateTimeOffset.UtcNow;

        // Use a transaction for bulk insert performance
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var product in products)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                await ExtractElementToSqliteAsync(connection, product, modelVersionId, extractedAt, cancellationToken);

                processedCount++;

                // Report progress every 100 items
                if (processedCount % 100 == 0)
                {
                    var percentComplete = 30 + (int)((processedCount / (double)totalProducts) * 40); // 30-70%
                    await NotifyProgressAsync(jobId, modelVersionId, "Extracting",
                        percentComplete, $"Extracted {processedCount}/{totalProducts} elements...", CancellationToken.None);
                }
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        // Get file size
        var fileInfo = new FileInfo(sqlitePath);
        return (processedCount, fileInfo.Length);
    }

    private static async Task CreateSqliteSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var createSchema = """
            CREATE TABLE IF NOT EXISTS Elements (
                Id TEXT PRIMARY KEY,
                ModelVersionId TEXT NOT NULL,
                EntityLabel INTEGER NOT NULL,
                GlobalId TEXT,
                Name TEXT,
                TypeName TEXT,
                Description TEXT,
                ObjectType TEXT,
                TypeObjectName TEXT,
                TypeObjectType TEXT,
                ExtractedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS PropertySets (
                Id TEXT PRIMARY KEY,
                ElementId TEXT NOT NULL,
                Name TEXT NOT NULL,
                GlobalId TEXT,
                IsTypePropertySet INTEGER NOT NULL,
                FOREIGN KEY (ElementId) REFERENCES Elements(Id)
            );

            CREATE TABLE IF NOT EXISTS Properties (
                Id TEXT PRIMARY KEY,
                PropertySetId TEXT NOT NULL,
                Name TEXT NOT NULL,
                Value TEXT,
                ValueType TEXT NOT NULL,
                Unit TEXT,
                FOREIGN KEY (PropertySetId) REFERENCES PropertySets(Id)
            );

            CREATE TABLE IF NOT EXISTS QuantitySets (
                Id TEXT PRIMARY KEY,
                ElementId TEXT NOT NULL,
                Name TEXT NOT NULL,
                GlobalId TEXT,
                FOREIGN KEY (ElementId) REFERENCES Elements(Id)
            );

            CREATE TABLE IF NOT EXISTS Quantities (
                Id TEXT PRIMARY KEY,
                QuantitySetId TEXT NOT NULL,
                Name TEXT NOT NULL,
                Value REAL,
                ValueType TEXT NOT NULL,
                Unit TEXT,
                FOREIGN KEY (QuantitySetId) REFERENCES QuantitySets(Id)
            );

            CREATE TABLE IF NOT EXISTS Metadata (
                Key TEXT PRIMARY KEY,
                Value TEXT
            );

            -- Indexes for efficient queries
            CREATE INDEX IF NOT EXISTS IX_Elements_ModelVersionId ON Elements(ModelVersionId);
            CREATE INDEX IF NOT EXISTS IX_Elements_EntityLabel ON Elements(EntityLabel);
            CREATE INDEX IF NOT EXISTS IX_Elements_GlobalId ON Elements(GlobalId);
            CREATE INDEX IF NOT EXISTS IX_Elements_TypeName ON Elements(TypeName);
            CREATE INDEX IF NOT EXISTS IX_PropertySets_ElementId ON PropertySets(ElementId);
            CREATE INDEX IF NOT EXISTS IX_PropertySets_Name ON PropertySets(Name);
            CREATE INDEX IF NOT EXISTS IX_Properties_PropertySetId ON Properties(PropertySetId);
            CREATE INDEX IF NOT EXISTS IX_Properties_Name ON Properties(Name);
            CREATE INDEX IF NOT EXISTS IX_QuantitySets_ElementId ON QuantitySets(ElementId);
            CREATE INDEX IF NOT EXISTS IX_Quantities_QuantitySetId ON Quantities(QuantitySetId);
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = createSchema;
        await command.ExecuteNonQueryAsync(cancellationToken);

        // Insert metadata
        command.CommandText = "INSERT INTO Metadata (Key, Value) VALUES ('SchemaVersion', '1.0')";
        await command.ExecuteNonQueryAsync(cancellationToken);

        command.CommandText = $"INSERT INTO Metadata (Key, Value) VALUES ('ExtractedAt', '{DateTimeOffset.UtcNow:O}')";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ExtractElementToSqliteAsync(
        SqliteConnection connection,
        IIfcProduct product,
        Guid modelVersionId,
        DateTimeOffset extractedAt,
        CancellationToken cancellationToken)
    {
        var elementId = Guid.NewGuid();

        // Insert element
        await using var elementCmd = connection.CreateCommand();
        elementCmd.CommandText = """
            INSERT INTO Elements (Id, ModelVersionId, EntityLabel, GlobalId, Name, TypeName, Description, ObjectType, TypeObjectName, TypeObjectType, ExtractedAt)
            VALUES ($id, $modelVersionId, $entityLabel, $globalId, $name, $typeName, $description, $objectType, $typeObjectName, $typeObjectType, $extractedAt)
            """;

        var typeRelation = product.IsTypedBy?.FirstOrDefault();
        var typeObject = typeRelation?.RelatingType;

        elementCmd.Parameters.AddWithValue("$id", elementId.ToString());
        elementCmd.Parameters.AddWithValue("$modelVersionId", modelVersionId.ToString());
        elementCmd.Parameters.AddWithValue("$entityLabel", product.EntityLabel);
        elementCmd.Parameters.AddWithValue("$globalId", (object?)product.GlobalId.ToString() ?? DBNull.Value);
        elementCmd.Parameters.AddWithValue("$name", (object?)GetLabelValue(product.Name) ?? DBNull.Value);
        elementCmd.Parameters.AddWithValue("$typeName", (object?)product.ExpressType.Name ?? DBNull.Value);
        elementCmd.Parameters.AddWithValue("$description", (object?)GetTextValue(product.Description) ?? DBNull.Value);
        elementCmd.Parameters.AddWithValue("$objectType", (object?)GetLabelValue(product.ObjectType) ?? DBNull.Value);
        elementCmd.Parameters.AddWithValue("$typeObjectName", (object?)GetLabelValue(typeObject?.Name) ?? DBNull.Value);
        elementCmd.Parameters.AddWithValue("$typeObjectType", (object?)typeObject?.ExpressType.Name ?? DBNull.Value);
        elementCmd.Parameters.AddWithValue("$extractedAt", extractedAt.ToString("O"));

        await elementCmd.ExecuteNonQueryAsync(cancellationToken);

        // Extract property sets
        var relDefines = product.IsDefinedBy.OfType<IIfcRelDefinesByProperties>().ToList();
        foreach (var rel in relDefines)
        {
            if (rel.RelatingPropertyDefinition is IIfcPropertySet pset)
            {
                await ExtractPropertySetToSqliteAsync(connection, pset, elementId, false, cancellationToken);
            }
            else if (rel.RelatingPropertyDefinition is IIfcElementQuantity qset)
            {
                await ExtractQuantitySetToSqliteAsync(connection, qset, elementId, cancellationToken);
            }
        }

        // Extract type property sets
        if (typeObject?.HasPropertySets != null)
        {
            foreach (var pset in typeObject.HasPropertySets.OfType<IIfcPropertySet>())
            {
                await ExtractPropertySetToSqliteAsync(connection, pset, elementId, true, cancellationToken);
            }
        }
    }

    private async Task ExtractPropertySetToSqliteAsync(
        SqliteConnection connection,
        IIfcPropertySet pset,
        Guid elementId,
        bool isTypePropertySet,
        CancellationToken cancellationToken)
    {
        var psetId = Guid.NewGuid();

        await using var psetCmd = connection.CreateCommand();
        psetCmd.CommandText = """
            INSERT INTO PropertySets (Id, ElementId, Name, GlobalId, IsTypePropertySet)
            VALUES ($id, $elementId, $name, $globalId, $isType)
            """;

        psetCmd.Parameters.AddWithValue("$id", psetId.ToString());
        psetCmd.Parameters.AddWithValue("$elementId", elementId.ToString());
        psetCmd.Parameters.AddWithValue("$name", GetLabelValue(pset.Name) ?? "Unnamed");
        psetCmd.Parameters.AddWithValue("$globalId", (object?)pset.GlobalId.ToString() ?? DBNull.Value);
        psetCmd.Parameters.AddWithValue("$isType", isTypePropertySet ? 1 : 0);

        await psetCmd.ExecuteNonQueryAsync(cancellationToken);

        // Insert properties
        foreach (var prop in pset.HasProperties)
        {
            await ExtractPropertyToSqliteAsync(connection, prop, psetId, cancellationToken);
        }
    }

    private async Task ExtractPropertyToSqliteAsync(
        SqliteConnection connection,
        IIfcProperty property,
        Guid psetId,
        CancellationToken cancellationToken)
    {
        var (value, valueType, unit) = ExtractPropertyValue(property);

        await using var propCmd = connection.CreateCommand();
        propCmd.CommandText = """
            INSERT INTO Properties (Id, PropertySetId, Name, Value, ValueType, Unit)
            VALUES ($id, $psetId, $name, $value, $valueType, $unit)
            """;

        propCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        propCmd.Parameters.AddWithValue("$psetId", psetId.ToString());
        propCmd.Parameters.AddWithValue("$name", GetIdentifierValue(property.Name) ?? "Unknown");
        propCmd.Parameters.AddWithValue("$value", (object?)value ?? DBNull.Value);
        propCmd.Parameters.AddWithValue("$valueType", valueType);
        propCmd.Parameters.AddWithValue("$unit", (object?)unit ?? DBNull.Value);

        await propCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ExtractQuantitySetToSqliteAsync(
        SqliteConnection connection,
        IIfcElementQuantity qset,
        Guid elementId,
        CancellationToken cancellationToken)
    {
        var qsetId = Guid.NewGuid();

        await using var qsetCmd = connection.CreateCommand();
        qsetCmd.CommandText = """
            INSERT INTO QuantitySets (Id, ElementId, Name, GlobalId)
            VALUES ($id, $elementId, $name, $globalId)
            """;

        qsetCmd.Parameters.AddWithValue("$id", qsetId.ToString());
        qsetCmd.Parameters.AddWithValue("$elementId", elementId.ToString());
        qsetCmd.Parameters.AddWithValue("$name", GetLabelValue(qset.Name) ?? "Unnamed");
        qsetCmd.Parameters.AddWithValue("$globalId", (object?)qset.GlobalId.ToString() ?? DBNull.Value);

        await qsetCmd.ExecuteNonQueryAsync(cancellationToken);

        // Insert quantities
        foreach (var qty in qset.Quantities)
        {
            await ExtractQuantityToSqliteAsync(connection, qty, qsetId, cancellationToken);
        }
    }

    private async Task ExtractQuantityToSqliteAsync(
        SqliteConnection connection,
        IIfcPhysicalQuantity quantity,
        Guid qsetId,
        CancellationToken cancellationToken)
    {
        var (value, valueType, unit) = ExtractQuantityValue(quantity);

        await using var qtyCmd = connection.CreateCommand();
        qtyCmd.CommandText = """
            INSERT INTO Quantities (Id, QuantitySetId, Name, Value, ValueType, Unit)
            VALUES ($id, $qsetId, $name, $value, $valueType, $unit)
            """;

        qtyCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        qtyCmd.Parameters.AddWithValue("$qsetId", qsetId.ToString());
        qtyCmd.Parameters.AddWithValue("$name", GetLabelValue(quantity.Name) ?? "Unknown");
        qtyCmd.Parameters.AddWithValue("$value", (object?)value ?? DBNull.Value);
        qtyCmd.Parameters.AddWithValue("$valueType", valueType);
        qtyCmd.Parameters.AddWithValue("$unit", (object?)unit ?? DBNull.Value);

        await qtyCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task PersistToOctopusDatabaseAsync(
        string sqlitePath,
        Guid modelVersionId,
        CancellationToken cancellationToken)
    {
        var connectionString = $"Data Source={sqlitePath}";
        await using var sqliteConn = new SqliteConnection(connectionString);
        await sqliteConn.OpenAsync(cancellationToken);

        // First, delete any existing properties for this model version (for re-processing)
        var existingElements = await _dbContext.IfcElements
            .Where(e => e.ModelVersionId == modelVersionId)
            .ToListAsync(cancellationToken);

        if (existingElements.Any())
        {
            _dbContext.IfcElements.RemoveRange(existingElements);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // Read from SQLite and persist to Octopus DB
        await using var elementsCmd = sqliteConn.CreateCommand();
        elementsCmd.CommandText = "SELECT * FROM Elements";
        await using var elementsReader = await elementsCmd.ExecuteReaderAsync(cancellationToken);

        var elements = new List<IfcElement>();
        var elementIdMap = new Dictionary<string, Guid>(); // SQLite ID -> Octopus ID

        while (await elementsReader.ReadAsync(cancellationToken))
        {
            var sqliteId = elementsReader.GetString(0);
            var element = new IfcElement
            {
                Id = Guid.NewGuid(),
                ModelVersionId = modelVersionId,
                EntityLabel = elementsReader.GetInt32(2),
                GlobalId = elementsReader.IsDBNull(3) ? null : elementsReader.GetString(3),
                Name = elementsReader.IsDBNull(4) ? null : elementsReader.GetString(4),
                TypeName = elementsReader.IsDBNull(5) ? null : elementsReader.GetString(5),
                Description = elementsReader.IsDBNull(6) ? null : elementsReader.GetString(6),
                ObjectType = elementsReader.IsDBNull(7) ? null : elementsReader.GetString(7),
                TypeObjectName = elementsReader.IsDBNull(8) ? null : elementsReader.GetString(8),
                TypeObjectType = elementsReader.IsDBNull(9) ? null : elementsReader.GetString(9),
                ExtractedAt = DateTimeOffset.Parse(elementsReader.GetString(10))
            };
            elements.Add(element);
            elementIdMap[sqliteId] = element.Id;
        }

        _dbContext.IfcElements.AddRange(elements);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Read and persist property sets
        await using var psetsCmd = sqliteConn.CreateCommand();
        psetsCmd.CommandText = "SELECT * FROM PropertySets";
        await using var psetsReader = await psetsCmd.ExecuteReaderAsync(cancellationToken);

        var propertySets = new List<IfcPropertySet>();
        var psetIdMap = new Dictionary<string, Guid>();

        while (await psetsReader.ReadAsync(cancellationToken))
        {
            var sqliteId = psetsReader.GetString(0);
            var sqliteElementId = psetsReader.GetString(1);

            if (!elementIdMap.TryGetValue(sqliteElementId, out var elementId))
                continue;

            var pset = new IfcPropertySet
            {
                Id = Guid.NewGuid(),
                ElementId = elementId,
                Name = psetsReader.GetString(2),
                GlobalId = psetsReader.IsDBNull(3) ? null : psetsReader.GetString(3),
                IsTypePropertySet = psetsReader.GetInt32(4) == 1
            };
            propertySets.Add(pset);
            psetIdMap[sqliteId] = pset.Id;
        }

        _dbContext.IfcPropertySets.AddRange(propertySets);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Read and persist properties
        await using var propsCmd = sqliteConn.CreateCommand();
        propsCmd.CommandText = "SELECT * FROM Properties";
        await using var propsReader = await propsCmd.ExecuteReaderAsync(cancellationToken);

        var properties = new List<IfcProperty>();

        while (await propsReader.ReadAsync(cancellationToken))
        {
            var sqlitePsetId = propsReader.GetString(1);

            if (!psetIdMap.TryGetValue(sqlitePsetId, out var psetId))
                continue;

            var prop = new IfcProperty
            {
                Id = Guid.NewGuid(),
                PropertySetId = psetId,
                Name = propsReader.GetString(2),
                Value = propsReader.IsDBNull(3) ? null : propsReader.GetString(3),
                ValueType = propsReader.GetString(4),
                Unit = propsReader.IsDBNull(5) ? null : propsReader.GetString(5)
            };
            properties.Add(prop);
        }

        _dbContext.IfcProperties.AddRange(properties);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Read and persist quantity sets
        await using var qsetsCmd = sqliteConn.CreateCommand();
        qsetsCmd.CommandText = "SELECT * FROM QuantitySets";
        await using var qsetsReader = await qsetsCmd.ExecuteReaderAsync(cancellationToken);

        var quantitySets = new List<IfcQuantitySet>();
        var qsetIdMap = new Dictionary<string, Guid>();

        while (await qsetsReader.ReadAsync(cancellationToken))
        {
            var sqliteId = qsetsReader.GetString(0);
            var sqliteElementId = qsetsReader.GetString(1);

            if (!elementIdMap.TryGetValue(sqliteElementId, out var elementId))
                continue;

            var qset = new IfcQuantitySet
            {
                Id = Guid.NewGuid(),
                ElementId = elementId,
                Name = qsetsReader.GetString(2),
                GlobalId = qsetsReader.IsDBNull(3) ? null : qsetsReader.GetString(3)
            };
            quantitySets.Add(qset);
            qsetIdMap[sqliteId] = qset.Id;
        }

        _dbContext.IfcQuantitySets.AddRange(quantitySets);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Read and persist quantities
        await using var qtysCmd = sqliteConn.CreateCommand();
        qtysCmd.CommandText = "SELECT * FROM Quantities";
        await using var qtysReader = await qtysCmd.ExecuteReaderAsync(cancellationToken);

        var quantities = new List<IfcQuantity>();

        while (await qtysReader.ReadAsync(cancellationToken))
        {
            var sqliteQsetId = qtysReader.GetString(1);

            if (!qsetIdMap.TryGetValue(sqliteQsetId, out var qsetId))
                continue;

            var qty = new IfcQuantity
            {
                Id = Guid.NewGuid(),
                QuantitySetId = qsetId,
                Name = qtysReader.GetString(2),
                Value = qtysReader.IsDBNull(3) ? null : qtysReader.GetDouble(3),
                ValueType = qtysReader.GetString(4),
                Unit = qtysReader.IsDBNull(5) ? null : qtysReader.GetString(5)
            };
            quantities.Add(qty);
        }

        _dbContext.IfcQuantities.AddRange(quantities);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Persisted {Elements} elements, {PSets} property sets, {Props} properties, {QSets} quantity sets, {Qtys} quantities to database",
            elements.Count, propertySets.Count, properties.Count, quantitySets.Count, quantities.Count);
    }

    private (string? value, string valueType, string? unit) ExtractPropertyValue(IIfcProperty property)
    {
        try
        {
            return property switch
            {
                IIfcPropertySingleValue singleValue => (
                    singleValue.NominalValue?.ToString(),
                    GetValueType(singleValue.NominalValue),
                    singleValue.Unit?.ToString()
                ),
                IIfcPropertyEnumeratedValue enumValue => (
                    enumValue.EnumerationValues != null
                        ? string.Join(", ", enumValue.EnumerationValues.Select(v => v.ToString()))
                        : null,
                    "enumeration",
                    null
                ),
                IIfcPropertyBoundedValue boundedValue => (
                    $"{boundedValue.LowerBoundValue?.ToString() ?? "?"} - {boundedValue.UpperBoundValue?.ToString() ?? "?"}",
                    "range",
                    boundedValue.Unit?.ToString()
                ),
                IIfcPropertyListValue listValue => (
                    listValue.ListValues != null
                        ? string.Join(", ", listValue.ListValues.Select(v => v.ToString()))
                        : null,
                    "list",
                    null
                ),
                IIfcPropertyTableValue => ("[Table]", "table", null),
                IIfcComplexProperty complexProp => ($"[{complexProp.HasProperties.Count()} properties]", "complex", null),
                _ => (property.ToString(), "unknown", null)
            };
        }
        catch
        {
            return (null, "unknown", null);
        }
    }

    private (double? value, string valueType, string? unit) ExtractQuantityValue(IIfcPhysicalQuantity quantity)
    {
        try
        {
            return quantity switch
            {
                IIfcQuantityLength length => (length.LengthValue, "length", length.Unit?.ToString() ?? "m"),
                IIfcQuantityArea area => (area.AreaValue, "area", area.Unit?.ToString() ?? "m2"),
                IIfcQuantityVolume volume => (volume.VolumeValue, "volume", volume.Unit?.ToString() ?? "m3"),
                IIfcQuantityCount count => (count.CountValue, "count", null),
                IIfcQuantityWeight weight => (weight.WeightValue, "weight", weight.Unit?.ToString() ?? "kg"),
                IIfcQuantityTime time => (time.TimeValue, "time", time.Unit?.ToString() ?? "s"),
                _ => (null, "unknown", null)
            };
        }
        catch
        {
            return (null, "unknown", null);
        }
    }

    private string GetValueType(IIfcValue? value)
    {
        if (value == null) return "null";

        var underlyingValue = value.Value;
        return underlyingValue switch
        {
            bool => "boolean",
            int or long => "integer",
            double or float => "double",
            string => "string",
            _ => "string"
        };
    }

    private string? GetLabelValue(object? label)
    {
        if (label == null) return null;
        var str = label.ToString();
        return string.IsNullOrEmpty(str) ? null : str;
    }

    private string? GetTextValue(object? text)
    {
        if (text == null) return null;
        var str = text.ToString();
        return string.IsNullOrEmpty(str) ? null : str;
    }

    private string? GetIdentifierValue(object? identifier)
    {
        if (identifier == null) return null;
        var str = identifier.ToString();
        return string.IsNullOrEmpty(str) ? null : str;
    }

    private async Task SetFailedStatusAsync(ModelVersion modelVersion, string errorMessage, CancellationToken cancellationToken)
    {
        modelVersion.ErrorMessage = $"Properties extraction failed: {errorMessage}";

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save error state for ModelVersion {ModelVersionId}", modelVersion.Id);
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
                Message = "Properties extraction completed successfully",
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
                Message = "Properties extraction failed",
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
