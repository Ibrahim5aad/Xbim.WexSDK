using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Xbim.WexServer.Abstractions.Auth;
using Xbim.WexServer.Abstractions.Processing;
using Xbim.WexServer.Abstractions.Storage;
using Xbim.WexServer.App.Auth;
using Xbim.WexServer.App.Endpoints;
using Xbim.WexServer.App.HealthChecks;
using Xbim.WexServer.App.Middleware;
using Xbim.WexServer.App.Processing;
using Xbim.WexServer.App.RateLimiting;
using Xbim.WexServer.Persistence.EfCore;
using Xbim.WexServer.Persistence.EfCore.Extensions;
using Xbim.WexServer.Processing;
using Xbim.WexServer.Storage.AzureBlob;
using Xbim.WexServer.Storage.LocalDisk;
using Xbim.WexServer.App.Swagger;
using Xbim.Common.Configuration;

var builder = WebApplication.CreateBuilder(args);

var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
try
{
    XbimServices.Current.ConfigureServices(s => s.AddXbimToolkit(c => c
        .AddLoggerFactory(loggerFactory)
        .AddGeometryServices()));
}
catch (InvalidOperationException)
{
    // XbimServices is a global singleton that can only be configured once.
    // In integration tests, WebApplicationFactory may create the host multiple times,
    // so we ignore this exception when services are already configured.
}

// Add service defaults (OpenTelemetry, health checks, resilience)
builder.AddServiceDefaults();

// Add persistence based on configuration
// Supported providers: "SqlServer" (default), "Sqlite"
// Skip if in Testing environment - tests configure their own in-memory database
if (!builder.Environment.EnvironmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase))
{
    var dbProvider = builder.Configuration.GetValue<string>("Database:Provider") ?? "SqlServer";
    var connectionString = builder.Configuration.GetConnectionString("XbimDb");

    if (dbProvider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        connectionString ??= "Data Source=Xbim.db";
        builder.Services.AddXbimSqlite(connectionString);
    }
    else
    {
        // Default to SQL Server
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                "SQL Server connection string 'XbimDb' is required. " +
                "Set Database:Provider to 'Sqlite' for local development without SQL Server.");
        }

        builder.Services.AddXbimSqlServer(connectionString);
    }
}

// Configure storage provider based on configuration
// Supported providers: "LocalDisk" (default), "AzureBlob"
var storageProvider = builder.Configuration.GetValue<string>("Storage:Provider") ?? "LocalDisk";

if (storageProvider.Equals("AzureBlob", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddAzureBlobStorage(builder.Configuration.GetSection("Storage:AzureBlob"));
}
else
{
    // Default to LocalDisk for development
    var basePath = builder.Configuration.GetValue<string>("Storage:LocalDisk:BasePath") ?? "Xbim-storage";
    builder.Services.AddLocalDiskStorage(basePath);
}

// Register progress notifier (default no-op implementation)
// Can be replaced with SignalR, webhooks, etc.
builder.Services.AddSingleton<IProgressNotifier>(NullProgressNotifier.Instance);

// Add health checks for DB and storage provider
// Skip DB health check in Testing environment (tests configure their own in-memory database)
if (!builder.Environment.EnvironmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<XbimDbContext>(
            name: "database",
            failureStatus: HealthStatus.Unhealthy,
            tags: ["db", "ready"])
        .AddCheck<StorageProviderHealthCheck>(
            name: "storage",
            failureStatus: HealthStatus.Unhealthy,
            tags: ["storage", "ready"]);
}

// Add processing queue and worker (in-memory Channel backend)
// Skip in Testing environment - tests configure their own mocks
if (!builder.Environment.EnvironmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddInMemoryProcessing(processing =>
    {
        processing.AddHandler<IfcToWexBimJobPayload, IfcToWexBimJobHandler>(IfcToWexBimJobHandler.JobTypeName);
        processing.AddHandler<ExtractPropertiesJobPayload, ExtractPropertiesJobHandler>(ExtractPropertiesJobHandler
            .JobTypeName);
    });
}

// Configure authentication mode based on configuration
var authMode = builder.Configuration.GetValue<string>("Auth:Mode") ?? "Development";

if (authMode.Equals("Development", StringComparison.OrdinalIgnoreCase))
{
    // Development auth mode: inject a fixed principal with PAT support
    builder.Services.AddXbimDevAuthWithPat(options =>
    {
        options.Subject = builder.Configuration.GetValue<string>("Auth:Dev:Subject") ?? "dev-user";
        options.Email = builder.Configuration.GetValue<string>("Auth:Dev:Email") ?? "dev@localhost";
        options.DisplayName = builder.Configuration.GetValue<string>("Auth:Dev:DisplayName") ?? "Development User";
    });
}
else if (authMode.Equals("OIDC", StringComparison.OrdinalIgnoreCase))
{
    // OIDC/JWT bearer auth mode: validate tokens via Authority + Audience with PAT support
    builder.Services.AddXbimOidcAuthWithPat(builder.Configuration);
}
else
{
    // For unknown modes, just add the user context service
    builder.Services.AddXbimUserContext();
    builder.Services.AddAuthorization();
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Xbim API",
        Version = "v1",
        Description = "BIM backend API for the Xbim platform"
    });

    // Remove default 200 OK response when 201 Created is defined
    // This fixes NSwag code generation for create endpoints
    options.OperationFilter<Remove200WhenCreatedOperationFilter>();

    // Add JWT Bearer authentication support in Swagger UI
    if (authMode.Equals("OIDC", StringComparison.OrdinalIgnoreCase))
    {
        options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Description = "Enter your JWT token in the format: Bearer {token}"
        });

        options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            {
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    {
                        Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    }
});

// Add rate limiting for upload endpoints
builder.Services.AddUploadRateLimiting(builder.Configuration);

// Add OAuth token service
builder.Services.Configure<Xbim.WexServer.App.Auth.OAuthTokenOptions>(
    builder.Configuration.GetSection("OAuth"));
builder.Services.AddSingleton<Xbim.WexServer.App.Auth.IOAuthTokenService, Xbim.WexServer.App.Auth.OAuthTokenService>();

var app = builder.Build();

// Apply pending migrations on startup (development convenience)
// Skip migrations for InMemory database (used in testing)
if (!app.Environment.EnvironmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<XbimDbContext>();
    dbContext.Database.Migrate();
}

// Global exception handler for authorization exceptions
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var exceptionHandlerFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        var exception = exceptionHandlerFeature?.Error;

        // Get correlation ID from context (will be set by CorrelationIdMiddleware)
        var correlationId = context.GetCorrelationId();

        if (exception is Xbim.WexServer.App.Auth.InsufficientScopeException scopeException)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "insufficient_scope",
                message = exception.Message,
                required_scopes = scopeException.RequiredScopes,
                present_scopes = scopeException.PresentScopes,
                correlationId
            });
        }
        else if (exception is Xbim.WexServer.App.Auth.WorkspaceIsolationException isolationException)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "workspace_isolation_violation",
                message = "Cross-workspace access is not permitted.",
                token_workspace_id = isolationException.TokenWorkspaceId,
                resource_workspace_id = isolationException.ResourceWorkspaceId,
                correlationId
            });
        }
        else if (exception is Xbim.WexServer.App.Auth.ForbiddenAccessException)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Forbidden",
                message = exception.Message,
                correlationId
            });
        }
        else if (exception is UnauthorizedAccessException)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Unauthorized",
                message = exception.Message,
                correlationId
            });
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Internal Server Error",
                message = app.Environment.IsDevelopment() ? exception?.Message : "An unexpected error occurred.",
                correlationId
            });
        }
    });
});

// Correlation ID middleware (adds request correlation for logging and tracing)
app.UseCorrelationId();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Xbim API v1");
    options.RoutePrefix = "swagger";
});

// Authentication and authorization middleware
app.UseAuthentication();
app.UseAuthorization();

// Rate limiting middleware
app.UseRateLimiter();

// User provisioning middleware (auto-creates User entity from authenticated principal)
app.UseUserProvisioning();

// Map Aspire default endpoints (/health, /alive)
app.MapDefaultEndpoints();

// Map API endpoints
app.MapWorkspaceEndpoints();
app.MapWorkspaceMembershipEndpoints();
app.MapProjectEndpoints();
app.MapProjectMembershipEndpoints();
app.MapFileUploadEndpoints();
app.MapFileEndpoints();
app.MapUsageEndpoints();
app.MapModelEndpoints();
app.MapModelVersionEndpoints();
app.MapPropertiesEndpoints();
app.MapOAuthAppEndpoints();
app.MapOAuthEndpoints();
app.MapPersonalAccessTokenEndpoints();

// Detailed health check endpoint with DB and storage provider status
app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    ResponseWriter = HealthCheckResponseWriter.WriteDetailedResponse
})
    .WithName("HealthCheck")
    .WithOpenApi();

// Debug endpoint to verify user context (development only)
app.MapGet("/api/v1/me", (IUserContext userContext) =>
    {
        if (!userContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(new
        {
            userId = userContext.UserId,
            subject = userContext.Subject,
            email = userContext.Email,
            displayName = userContext.DisplayName,
            isAuthenticated = userContext.IsAuthenticated
        });
    })
    .WithName("GetCurrentUser")
    .WithOpenApi()
    .RequireAuthorization();

app.Run();

// Make the implicit Program class public so it can be used in integration tests
public partial class Program
{
}