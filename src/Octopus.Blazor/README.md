# Octopus.Blazor

[![NuGet](https://img.shields.io/nuget/v/Octopus.Blazor.svg?style=flat-square)](https://www.nuget.org/packages/Octopus.Blazor/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Octopus.Blazor.svg?style=flat-square)](https://www.nuget.org/packages/Octopus.Blazor/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A Blazor component library for building BIM (Building Information Modeling) applications. Wraps the [@xbim/viewer](https://www.npmjs.com/package/@xbim/viewer) JavaScript library for 3D model visualization in Blazor WebAssembly or Server applications.

## Features

- **3D BIM Viewer** - WebGL-based visualization of wexBIM models
- **Plugin System** - Navigation cube, grid, section box, clipping planes
- **Sidebar Docking** - Dockable/overlay panels with icon bar navigation
- **Property Display** - Multi-source property aggregation (IFC, database, custom)
- **Model Hierarchy** - Product types and spatial structure navigation
- **Theming** - Light/dark themes with customizable colors
- **Direct IFC Loading** - Server-side conversion (Blazor Server only)

## Two Modes of Operation

This library supports two modes to fit different application needs:

| Mode | Description | Best For |
|------|-------------|----------|
| **Standalone** | Self-contained viewer with no backend dependencies | Demos, embedded viewers, simple apps, prototypes |
| **Platform** | Integrated with Octopus Server for full model management | Production apps, team collaboration, cloud storage |

## Installation

```bash
dotnet add package Octopus.Blazor
```

For platform mode, also install the API client:

```bash
dotnet add package Octopus.Api.Client
```

## Quick Start

### Standalone Mode

Register services for standalone operation (no server required):

```csharp
// Program.cs
builder.Services.AddOctopusBlazorStandalone();
```

Add to `_Imports.razor`:

```razor
@using Octopus.Blazor
@using Octopus.Blazor.Components
```

Basic viewer:

```razor
<OctopusViewer Id="viewer"
               Width="800"
               Height="600"
               ModelUrl="models/building.wexbim"
               OnModelLoaded="HandleModelLoaded" />

@code {
    private void HandleModelLoaded(bool success)
    {
        Console.WriteLine(success ? "Model loaded" : "Load failed");
    }
}
```

### Platform Mode

Register services with Octopus Server connection:

```csharp
// Program.cs
builder.Services.AddOctopusClient(options =>
{
    options.BaseUrl = "https://your-octopus-server.com";
});
builder.Services.AddOctopusBlazorPlatform();
```

Platform mode enables:
- Model storage in cloud or on-premises
- Model versioning and history
- Property extraction stored in database
- User authentication and workspace management

## Components

### OctopusViewer

The main viewer component with full model interaction support.

```razor
<OctopusViewer Id="myViewer"
               Width="100%"
               Height="600"
               BackgroundColor="#F5F5F5"
               ModelUrl="models/SampleModel.wexbim"
               OnViewerInitialized="HandleInit"
               OnModelLoaded="HandleLoad"
               OnPick="HandlePick">
    <!-- Child components go here -->
</OctopusViewer>
```

**Parameters:**
- `Id` - Unique viewer identifier
- `Width`/`Height` - Dimensions (px or %)
- `BackgroundColor` - Canvas background
- `ModelUrl` - Initial model URL to load

**Events:**
- `OnViewerInitialized` - Viewer ready
- `OnModelLoaded` - Model load complete
- `OnPick` - Element selected
- `OnHoverPick` - Element hovered

### ViewerToolbar

Built-in toolbar with common viewer operations.

```razor
<OctopusViewer ...>
    <ViewerToolbar Position="ToolbarPosition.Top"
                   ShowResetView="true"
                   ShowZoomControls="true"
                   ShowNavigationModes="true" />
</OctopusViewer>
```

### ViewerSidebar + SidebarPanel

Dockable sidebar system with icon-based panel management.

```razor
<OctopusViewer ...>
    <ViewerSidebar Position="SidebarPosition.Right" DefaultMode="SidebarMode.Docked">
        <SidebarPanel Title="Properties" Icon="bi-info-circle">
            <PropertiesPanel ShowHeader="false" />
        </SidebarPanel>
        <SidebarPanel Title="Hierarchy" Icon="bi-diagram-3">
            <ModelHierarchyPanel ShowHeader="false" />
        </SidebarPanel>
    </ViewerSidebar>
</OctopusViewer>
```

### PropertiesPanel

Displays element properties when selected. Auto-subscribes to viewer pick events.

```razor
<PropertiesPanel ShowHeader="true" />
```

### ModelHierarchyPanel

Shows model structure with Product Types and Spatial Structure tabs.

```razor
<ModelHierarchyPanel ShowHeader="true" />
```

### FileLoaderPanel

UI for loading models from URLs, files, or demo assets.

```razor
<FileLoaderPanel AllowIfcFiles="true" OnFileLoaded="HandleFile" />
```

## Plugins

Add viewer plugins for enhanced functionality:

```razor
<OctopusViewer @ref="_viewer" ...>
    <NavigationCubePlugin Opacity="0.7" />
    <GridPlugin Spacing="1000" Color="#CCCCCC" />
    <SectionBoxPlugin />
    <ClippingPlanePlugin />
</OctopusViewer>
```

## Theming

Register and configure the theme service:

```csharp
// Program.cs
var themeService = new ThemeService();
themeService.SetTheme(ViewerTheme.Dark);
themeService.SetAccentColors(lightColor: "#0969da", darkColor: "#4da3ff");
builder.Services.AddSingleton(themeService);
```

Toggle theme at runtime:

```razor
@inject ThemeService ThemeService

<button @onclick="() => ThemeService.ToggleTheme()">Toggle Theme</button>
```

## Property Sources

The library supports multiple property sources for element data.

### IFC Property Source (Blazor Server)

```csharp
var model = IfcStore.Open("model.ifc");
var propertySource = new IfcPropertySource(model, viewerModelId);
propertyService.RegisterSource(propertySource);
```

### Custom Property Source

```csharp
var apiSource = new CustomPropertySource(
    async (query, ct) =>
    {
        var data = await api.GetPropertiesAsync(query.ElementId);
        return new ElementProperties { /* ... */ };
    },
    sourceType: "REST API",
    name: "API Properties"
);
propertyService.RegisterSource(apiSource);
```

### Dictionary Property Source

```csharp
var dictSource = new DictionaryPropertySource(name: "Custom");
dictSource.AddProperty(elementId: 123, modelId: 0,
    groupName: "Status", propertyName: "Approved", value: "Yes");
propertyService.RegisterSource(dictSource);
```

## IFC Loading (Blazor Server Only)

Direct IFC file loading with automatic wexBIM conversion:

```csharp
// Program.cs
builder.Services.AddSingleton<IfcModelService>();
builder.Services.AddSingleton<IfcHierarchyService>();
```

```csharp
@inject IfcModelService IfcService

var result = await IfcService.ProcessIfcBytesAsync(ifcData, "model.ifc");
if (result.Success)
{
    await Viewer.LoadModelFromBytesAsync(result.WexbimData!, "model.ifc");
}
```

## Service Registration

### Standalone Mode

For viewer applications without a backend server:

```csharp
builder.Services.AddOctopusBlazorStandalone();
```

With configuration:

```csharp
builder.Services.AddOctopusBlazorStandalone(options =>
{
    options.DefaultTheme = ViewerTheme.Dark;
});
```

### Platform Mode

For applications connected to Octopus Server:

```csharp
builder.Services.AddOctopusClient(options =>
{
    options.BaseUrl = "https://your-server.com";
});
builder.Services.AddOctopusBlazorPlatform();
```

Platform mode automatically configures property sources to fetch from the server and enables cloud-based model loading.

## Requirements

- .NET 9.0+
- Blazor WebAssembly or Blazor Server

## License

MIT

## Related Packages

- [Octopus.Api.Client](https://www.nuget.org/packages/Octopus.Api.Client/) - API client for Octopus Server
