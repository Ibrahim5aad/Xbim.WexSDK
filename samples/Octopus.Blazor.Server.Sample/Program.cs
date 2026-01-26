using Octopus.Blazor;
using Octopus.Blazor.Services.Abstractions;
using Octopus.Blazor.Services.WexBimSources;
using Octopus.Blazor.Models;
using Octopus.Blazor.Server.Sample;
using Xbim.Common.Configuration;
using Xbim.Ifc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure xBIM toolkit
var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
XbimServices.Current.ConfigureServices(s => s.AddXbimToolkit(c => c.AddLoggerFactory(loggerFactory)));

// Add Octopus.Blazor server services with configuration
// This includes standalone services + IFC processing capabilities
builder.Services.AddOctopusBlazorServer(options =>
{
    options.InitialTheme = ViewerTheme.Dark;
    options.LightAccentColor = "#0969da";
    options.DarkAccentColor = "#1e7e34";
    options.LightBackgroundColor = "#ffffff";
    options.DarkBackgroundColor = "#404040";

    // Configure standalone WexBIM sources from wwwroot and local files
    options.StandaloneSources = new StandaloneSourceOptions()
        .AddStaticAsset("models/FourWalls.wexbim", "Four Walls (WexBIM)")
        .AddStaticAsset("models/SampleHouse.ifc", "Sample House (IFC)");
});

// Add HttpClient for static asset loading
builder.Services.AddHttpClient("StaticAssets", client =>
{
    // Base address will be set at runtime
});

var app = builder.Build();

// Initialize WexBIM sources after build
var sourceProvider = app.Services.GetRequiredService<IWexBimSourceProvider>();
var blazorOptions = app.Services.GetService<OctopusBlazorOptions>();

// Register local file sources from wwwroot (server can access filesystem directly)
var wwwrootPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
if (blazorOptions?.StandaloneSources != null)
{
    foreach (var assetConfig in blazorOptions.StandaloneSources.StaticAssets)
    {
        // For Blazor Server, we can load directly from disk
        var fullPath = Path.Combine(wwwrootPath, assetConfig.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath) && fullPath.EndsWith(".wexbim", StringComparison.OrdinalIgnoreCase))
        {
            var source = new LocalFileWexBimSource(fullPath, assetConfig.Name);
            sourceProvider.RegisterSource(source);
        }
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

// Configure static files with explicit content types for .wexbim
var contentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".wexbim"] = "application/octet-stream";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypeProvider,
    ServeUnknownFileTypes = true,
    DefaultContentType = "application/octet-stream"
});

app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
