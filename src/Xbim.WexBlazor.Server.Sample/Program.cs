using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Xbim.WexBlazor.Services;
using Xbim.WexBlazor.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

var themeService = new ThemeService();
themeService.SetTheme(ViewerTheme.Dark);
themeService.SetAccentColors(lightColor: "#0969da", darkColor: "#1e7e34");
themeService.SetBackgroundColors(lightColor: "#ffffff", darkColor: "#404040");
builder.Services.AddSingleton(themeService);

// Register IFC processing services (server-side only)
builder.Services.AddSingleton<IfcModelService>();
builder.Services.AddSingleton<PropertyService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
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

app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
