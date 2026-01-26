# Octopus.Blazor

[![NuGet](https://img.shields.io/nuget/v/Octopus.Blazor.svg?style=flat-square)](https://www.nuget.org/packages/Octopus.Blazor/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Octopus.Blazor.svg?style=flat-square)](https://www.nuget.org/packages/Octopus.Blazor/)
[![Build Status](https://github.com/Ibrahim5aad/Octopus/actions/workflows/publish-nuget.yml/badge.svg)](https://github.com/Ibrahim5aad/Octopus/actions/workflows/publish-nuget.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A Blazor component library that wraps the @xbim/viewer JavaScript library for use in Blazor applications. This library allows you to display 3D building models in the wexBIM format. **Blazor Server applications can also load IFC files directly**, which are automatically converted to wexBIM format for visualization.

![Octopus.Blazor in action](screenshot.png)

## Project Structure

- **Octopus.Blazor**: The component library project
  - **Components/**: Blazor components (Viewer, Sidebar, Panels, etc.)
  - **Interop/**: JavaScript interop services
  - **Models/**: Data models and enums
  - **Services/**: Services (Theme, Properties, IFC processing, etc.)
  - **wwwroot/js/**: JavaScript interop modules
  - **wwwroot/lib/**: Third-party libraries (xBIM Viewer)

- **Octopus.Blazor.Sample**: WebAssembly sample application showcasing the library
- **Octopus.Blazor.Server.Sample**: Blazor Server sample application with IFC file loading support

## Features

- Base JavaScript interop infrastructure
- Structured approach to wrapping JavaScript libraries
- xBIM Viewer component for displaying wexBIM 3D models
- Controls for loading models, zooming, and manipulating the view
- **Direct IFC file loading in Blazor Server applications** - automatically converts IFC files to wexBIM format
- Extensible properties system for displaying element properties from multiple sources
- Theme support (light/dark) with customizable accent colors
- **Sidebar docking system** - dockable panels with overlay and docked modes
- **Model hierarchy panel** - view product types and spatial structure of loaded models

## Installation

Install the package from NuGet:

```bash
dotnet add package Octopus.Blazor
```

Or via the Visual Studio Package Manager:

```
Install-Package Octopus.Blazor
```

## Getting Started

### Using the Components

Then add the following to your `_Imports.razor` file:

```razor
@using Octopus.Blazor
@using Octopus.Blazor.Components
```

Example usage of the Octopus Viewer component:

```razor
<OctopusViewer Id="myViewer"
                    Width="800"
                    Height="600"
                    BackgroundColor="#F5F5F5"
                    ModelUrl="models/SampleModel.wexbim"
                    OnViewerInitialized="HandleViewerInitialized"
                    OnModelLoaded="HandleModelLoaded" />

@code {
    private async Task HandleViewerInitialized(string viewerId)
    {
        // The viewer has been initialized
    }
    
    private async Task HandleModelLoaded(bool success)
    {
        // The model has been loaded
    }
}
```

## Theming and Customization

The library supports light and dark themes with customizable accent colors.

### Setting Up the Theme Service

Register the `ThemeService` in your `Program.cs`:

```csharp
using Octopus.Blazor.Services;
using Octopus.Blazor.Models;

var themeService = new ThemeService();
themeService.SetTheme(ViewerTheme.Dark);
themeService.SetAccentColors(
    lightColor: "#0969da",
    darkColor: "#4da3ff"
);
builder.Services.AddSingleton(themeService);
```

### Using the Theme Service

Inject and use the theme service in your components:

```razor
@inject ThemeService ThemeService

<button @onclick="ToggleTheme">Toggle Theme</button>

@code {
    private void ToggleTheme()
    {
        ThemeService.ToggleTheme();
    }
    
    protected override async Task OnInitializedAsync()
    {
        ThemeService.OnThemeChanged += StateHasChanged;
    }
}
```

### Customizing Accent Colors

You can change the accent colors at runtime:

```csharp
ThemeService.SetAccentColors(
    lightColor: "#d73a49",
    darkColor: "#ff6b6b"
);
```

The accent color is used for:
- Highlighted elements
- Active buttons
- Selected navigation modes
- Button group labels
- Focus indicators

### Customizing Selection and Hover Colors

You can customize the colors used when elements are selected or hovered:

```csharp
ThemeService.SetSelectionAndHoverColors(
    selectionColor: "#ff6b6b",  // Red for selection
    hoverColor: "#4da3ff"        // Blue for hover
);
```

## Element Properties

The library provides a flexible properties system that allows displaying element properties from various sources.

### Property Sources

Property sources provide element properties from different data sources:

1. **IfcPropertySource**: Reads properties directly from IFC models using xBIM Essentials
2. **DictionaryPropertySource**: Stores properties in memory (great for demos or caching)
3. **CustomPropertySource**: Integrates with any custom data source (databases, APIs, etc.)

### Setting Up IFC Property Source

To read properties from IFC files, you need to load the IFC model using xBIM Essentials:

```csharp
using Xbim.Ifc;
using Octopus.Blazor.Services;

// Load IFC model
var model = IfcStore.Open("path/to/model.ifc");

// Create property source (viewerModelId is the ID returned when loading the wexbim file)
var propertySource = new IfcPropertySource(model, viewerModelId);

// Register with the property service
propertyService.RegisterSource(propertySource);
```

### Using the Properties Panel

The `PropertiesPanel` auto-injects `PropertyService` and subscribes to pick events:

```razor
<OctopusViewer @ref="_viewer" ...>
    <PropertiesPanel />
</OctopusViewer>
```

### Custom Property Source Example

Create a custom property source for integration with databases or APIs using `CustomPropertySource`:

```csharp
// In Program.cs
var propertyService = new PropertyService();

var apiPropertySource = new CustomPropertySource(
    async (query, ct) =>
    {
        // Simulate API latency
        await Task.Delay(100, ct);
        
        // Fetch properties from your database/API
        var data = await myApiClient.GetElementPropertiesAsync(query.ElementId, query.ModelId);
        
        return new ElementProperties
        {
            ElementId = query.ElementId,
            ModelId = query.ModelId,
            Name = data.Name,
            TypeName = data.TypeName,
            Groups = new List<PropertyGroup>
            {
                new PropertyGroup
                {
                    Name = "API Data",
                    Source = "REST API",
                    Properties = data.Properties.Select(p => new PropertyValue
                    {
                        Name = p.Name,
                        Value = p.Value,
                        ValueType = p.ValueType
                    }).ToList()
                }
            }
        };
    },
    sourceType: "REST API",
    name: "API Properties"
);

propertyService.RegisterSource(apiPropertySource);
builder.Services.AddSingleton(propertyService);
```

The `CustomPropertySource` constructor accepts:
- `propertyProvider`: An async function that takes a `PropertyQuery` and returns `ElementProperties?`
- `sourceType`: A string identifier for the source (e.g., "Database", "REST API")
- `modelIds`: Optional array of model IDs this source supports (empty = all models)
- `name`: Display name for the source

### In-Memory Properties (Dictionary Source)

For simple use cases or demos:

```csharp
var dictSource = new DictionaryPropertySource(name: "Demo Properties");

// Add properties for specific elements
dictSource.AddProperty(elementId: 123, modelId: 0, 
    groupName: "Custom", 
    propertyName: "Status", 
    value: "Approved");

propertyService.RegisterSource(dictSource);
```

## Sidebar Docking System

The library includes a flexible sidebar system that allows you to dock panels alongside the viewer or display them as overlays.

### Using ViewerSidebar

The `ViewerSidebar` component provides a dockable sidebar with icon-based panel management. Panels inside the sidebar automatically receive the `OctopusViewer` via cascading parameters:

```razor
<OctopusViewer @ref="_viewer" ...>
    <ViewerSidebar Position="SidebarPosition.Left">
        <SidebarPanel Title="Properties" Icon="bi-info-circle" @bind-IsOpen="_showProperties">
            <PropertiesPanel ShowHeader="false" />
        </SidebarPanel>
        
        <SidebarPanel Title="Hierarchy" Icon="bi-diagram-3" @bind-IsOpen="_showHierarchy">
            <ModelHierarchyPanel ShowHeader="false" />
        </SidebarPanel>
    </ViewerSidebar>
</OctopusViewer>
```

Both `PropertiesPanel` and `ModelHierarchyPanel` are self-contained - they automatically inject their required services and subscribe to viewer events.

### Sidebar Features

- **Docked Mode**: Panels are docked alongside the viewer, resizing the canvas automatically
- **Overlay Mode**: Panels appear as overlays on top of the viewer
- **Icon Bar**: Quick access to panels via icon buttons
- **Single Panel**: Only one panel is visible at a time
- **Dynamic Resizing**: Canvas automatically adjusts when panels are docked

### SidebarPanel Component

Each panel within the sidebar is wrapped in a `SidebarPanel` component:

```razor
<SidebarPanel Title="My Panel" Icon="bi-gear" IsOpen="@_isOpen">
    <!-- Your panel content -->
</SidebarPanel>
```

The `SidebarPanel` provides:
- Toggle dock/overlay button
- Close button
- Automatic registration with parent `ViewerSidebar`

## Model Hierarchy Panel

The `ModelHierarchyPanel` displays model structure in two tabs:

- **Product Types**: IFC entity types with element counts
- **Spatial Structure**: Project → Site → Building → Storey hierarchy (Blazor Server only)

The panel auto-refreshes when models are loaded/unloaded and supports multiple models with collapsible headers.

```razor
<OctopusViewer @ref="_viewer" ...>
    <ModelHierarchyPanel />
</OctopusViewer>
```

For spatial structure (Blazor Server only), register `IfcHierarchyService`:

```csharp
// Program.cs
builder.Services.AddSingleton<IfcHierarchyService>();
```

## Loading IFC Files Directly

**⚠️ Important: IFC file loading is only available in Blazor Server applications**, not in Blazor WebAssembly.

The library supports loading IFC files directly and automatically converting them to wexBIM format for visualization. This functionality uses the [xBIM Geometry Engine v6](https://github.com/xBimTeam/XbimGeometry/tree/feature/netcore) and requires server-side execution (Blazor Server or ASP.NET Core API).

### Why Server-Side Only?

IFC processing requires native code from the xBIM Geometry Engine, which cannot run in WebAssembly browsers. For Blazor WebAssembly applications, you'll need to:
- Pre-convert IFC files to wexBIM format, or
- Create a server-side API endpoint to process IFC files and return wexbim data

### Setting Up IFC Loading (Blazor Server)

```csharp
// In Program.cs
builder.Services.AddSingleton<IfcModelService>();
builder.Services.AddSingleton<PropertyService>();
```

### Processing IFC Files

```csharp
@inject IfcModelService IfcService
@inject PropertyService PropertyService

@code {
    private async Task LoadIfcFile(byte[] ifcData, string fileName)
    {
        // Process the IFC file - generates wexbim and keeps IModel for properties
        var result = await IfcService.ProcessIfcBytesAsync(
            ifcData, 
            fileName,
            new Progress<IfcProcessingProgress>(p => 
            {
                Console.WriteLine($"{p.Stage}: {p.Message} ({p.PercentComplete}%)");
            }));
        
        if (result.Success && result.WexbimData != null)
        {
            // Load the generated wexbim into the viewer
            var loadedModel = await Viewer.LoadModelFromBytesAsync(
                result.WexbimData, 
                fileName);
            
            if (loadedModel != null && result.Model != null)
            {
                // Store the IFC model reference for property access
                loadedModel.IfcModel = result.Model;
                loadedModel.OriginalFormat = ModelFormat.Ifc;
                
                // Register property source for this model
                var propertySource = new IfcPropertySource(result.Model, loadedModel.Id);
                PropertyService.RegisterSource(propertySource);
            }
        }
    }
}
```

### FileLoaderPanel with IFC Support

The `FileLoaderPanel` component automatically supports IFC files when `AllowIfcFiles="true"`:

```razor
<FileLoaderPanel 
    IsVisible="true"
    AllowIfcFiles="true"
    OnFileLoaded="HandleFileLoaded" />
```

The panel will:
- Accept `.ifc`, `.ifczip`, and `.wexbim` files
- Show appropriate icons and badges for each format
- Display a note when IFC files will be processed


## wexBIM Format

The viewer requires 3D models in the wexBIM format. You can convert IFC models to wexBIM using tools from the [XbimEssentials](https://github.com/xBimTeam/XbimEssentials) library, or use the built-in `IfcModelService` (server-side only).

## Development

To build and run the sample project:

```bash
git clone https://github.com/Ibrahim5aad/Octopus.git
cd Octopus
dotnet build
dotnet run --project src/Octopus.Blazor.Sample/Octopus.Blazor.Sample.csproj
```

## License

MIT

## Acknowledgements

This project uses the [@xbim/viewer](https://www.npmjs.com/package/@xbim/viewer) JavaScript library from the [xBimTeam](https://github.com/xBimTeam/XbimWebUI). 
