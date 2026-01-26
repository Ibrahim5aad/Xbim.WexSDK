using Microsoft.EntityFrameworkCore;
using Octopus.Server.Persistence.EfCore;
using Octopus.Server.Persistence.EfCore.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults (OpenTelemetry, health checks, resilience)
builder.AddServiceDefaults();

// Add persistence (SQLite for development by default)
var connectionString = builder.Configuration.GetConnectionString("OctopusDb") ?? "Data Source=octopus.db";
builder.Services.AddOctopusSqlite(connectionString);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Octopus Server API",
        Version = "v1",
        Description = "BIM backend API for the Octopus platform"
    });
});

var app = builder.Build();

// Apply pending migrations on startup (development convenience)
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<OctopusDbContext>();
    dbContext.Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Octopus Server API v1");
    options.RoutePrefix = "swagger";
});

// Map Aspire default endpoints (/health, /alive)
app.MapDefaultEndpoints();

app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }))
   .WithName("HealthCheck")
   .WithOpenApi();

app.Run();
