using Microsoft.EntityFrameworkCore;
using Octopus.Server.Abstractions.Auth;
using Octopus.Server.Abstractions.Processing;
using Octopus.Server.Abstractions.Storage;
using Octopus.Server.App.Auth;
using Octopus.Server.App.Endpoints;
using Octopus.Server.App.Processing;
using Octopus.Server.Persistence.EfCore;
using Octopus.Server.Persistence.EfCore.Extensions;
using Octopus.Server.Processing;
using Octopus.Server.Storage.AzureBlob;
using Octopus.Server.Storage.LocalDisk;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults (OpenTelemetry, health checks, resilience)
builder.AddServiceDefaults();

// Add persistence (SQLite for development by default)
// Skip if in Testing environment - tests configure their own in-memory database
if (!builder.Environment.EnvironmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase))
{
    var connectionString = builder.Configuration.GetConnectionString("OctopusDb") ?? "Data Source=octopus.db";
    builder.Services.AddOctopusSqlite(connectionString);
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
    var basePath = builder.Configuration.GetValue<string>("Storage:LocalDisk:BasePath") ?? "octopus-storage";
    builder.Services.AddLocalDiskStorage(basePath);
}

// Register progress notifier (default no-op implementation)
// Can be replaced with SignalR, webhooks, etc.
builder.Services.AddSingleton<IProgressNotifier>(NullProgressNotifier.Instance);

// Add processing queue and worker (in-memory Channel backend)
// Skip in Testing environment - tests configure their own mocks
if (!builder.Environment.EnvironmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddInMemoryProcessing(processing =>
    {
        processing.AddHandler<IfcToWexBimJobPayload, IfcToWexBimJobHandler>(IfcToWexBimJobHandler.JobTypeName);
    });
}

// Configure authentication mode based on configuration
var authMode = builder.Configuration.GetValue<string>("Auth:Mode") ?? "Development";

if (authMode.Equals("Development", StringComparison.OrdinalIgnoreCase))
{
    // Development auth mode: inject a fixed principal
    builder.Services.AddOctopusDevAuth(options =>
    {
        options.Subject = builder.Configuration.GetValue<string>("Auth:Dev:Subject") ?? "dev-user";
        options.Email = builder.Configuration.GetValue<string>("Auth:Dev:Email") ?? "dev@localhost";
        options.DisplayName = builder.Configuration.GetValue<string>("Auth:Dev:DisplayName") ?? "Development User";
    });
}
else if (authMode.Equals("OIDC", StringComparison.OrdinalIgnoreCase))
{
    // OIDC/JWT bearer auth mode: validate tokens via Authority + Audience
    builder.Services.AddOctopusOidcAuth(builder.Configuration);
}
else
{
    // For unknown modes, just add the user context service
    builder.Services.AddOctopusUserContext();
    builder.Services.AddAuthorization();
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Octopus Server API",
        Version = "v1",
        Description = "BIM backend API for the Octopus platform"
    });

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

var app = builder.Build();

// Apply pending migrations on startup (development convenience)
// Skip migrations for InMemory database (used in testing)
if (!app.Environment.EnvironmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();
    dbContext.Database.Migrate();
}

// Global exception handler for authorization exceptions
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var exceptionHandlerFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        var exception = exceptionHandlerFeature?.Error;

        if (exception is Octopus.Server.App.Auth.ForbiddenAccessException)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Forbidden",
                message = exception.Message
            });
        }
        else if (exception is UnauthorizedAccessException)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Unauthorized",
                message = exception.Message
            });
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Internal Server Error",
                message = app.Environment.IsDevelopment() ? exception?.Message : "An unexpected error occurred."
            });
        }
    });
});

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Octopus Server API v1");
    options.RoutePrefix = "swagger";
});

// Authentication and authorization middleware
app.UseAuthentication();
app.UseAuthorization();

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

app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }))
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
public partial class Program { }
