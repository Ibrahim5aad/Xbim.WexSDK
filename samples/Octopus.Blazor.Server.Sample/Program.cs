using Octopus.Blazor;
using Octopus.Blazor.Server.Sample;
using Xbim.Common.Configuration;
using Xbim.Ifc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure xBIM toolkit with geometry services
var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
XbimServices.Current.ConfigureServices(s => s.AddXbimToolkit(c => c
    .AddLoggerFactory(loggerFactory)
    .AddGeometryServices()));

// Add Octopus.Blazor server services using configuration binding from appsettings.json.
// This reads from the "Octopus:Standalone" section for theme and FileLoaderPanel settings.
// See appsettings.json for the configuration structure.
builder.Services.AddOctopusBlazorServer(builder.Configuration);

var app = builder.Build();

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
