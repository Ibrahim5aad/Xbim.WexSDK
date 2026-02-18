using Xbim.WexBlazor;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults (OpenTelemetry, health checks, service discovery, resilience)
builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure Xbim.WexBlazor with PlatformConnected mode using configuration
// In Development auth mode, no token is required
// When running via AppHost, service discovery resolves "http://Xbim-server" to the actual endpoint
builder.Services.AddXbimBlazorPlatformConnected(builder.Configuration);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

// Map Aspire default endpoints (/health, /alive)
app.MapDefaultEndpoints();

app.MapRazorComponents<Xbim.Web.App>()
    .AddInteractiveServerRenderMode();

app.Run();
