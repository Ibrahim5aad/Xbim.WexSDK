using Xbim.WexBlazor.Services;
using Xbim.WexBlazor.Models;
using Xbim.WexBlazor.Server.Sample;
using Xbim.Common.Configuration;
using Xbim.Ifc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
XbimServices.Current.ConfigureServices(s => s.AddXbimToolkit(c => c.AddLoggerFactory(loggerFactory)));

var themeService = new ThemeService();
themeService.SetTheme(ViewerTheme.Dark);
themeService.SetAccentColors(lightColor: "#0969da", darkColor: "#1e7e34");
themeService.SetBackgroundColors(lightColor: "#ffffff", darkColor: "#404040");
builder.Services.AddSingleton(themeService);

builder.Services.AddSingleton<IfcModelService>();
builder.Services.AddSingleton<PropertyService>();
builder.Services.AddSingleton<IfcHierarchyService>();

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
