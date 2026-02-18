# Xbim SDK

[![Build Status](https://github.com/Ibrahim5aad/Xbim/actions/workflows/ci.yml/badge.svg)](https://github.com/Ibrahim5aad/Xbim/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

An open-source SDK and scaffold for building BIM (Building Information Modeling) applications with .NET 9. Xbim provides reusable components, a REST API server, and client libraries to accelerate the development of custom BIM solutions.

| Package | GitHub Packages |
|---------|-----------------|
| Xbim.WexBlazor | [View Package](https://github.com/Ibrahim5aad/Xbim/pkgs/nuget/Xbim.WexBlazor) |
| Xbim.WexServer.Client | [View Package](https://github.com/Ibrahim5aad/Xbim/pkgs/nuget/Xbim.WexServer.Client) |

![Xbim Viewer](screenshot.png)

## What is Xbim?

Xbim is a toolkit for developers building BIM applications. It provides:

- **Blazor Component Library** - Drop-in 3D viewer components for visualizing IFC/wexBIM models
- **REST API Server** - Ready-to-deploy backend for model storage, processing, and management
- **Generated API Client** - Typed HTTP client for seamless server integration
- **Reference Implementation** - Full web application demonstrating all capabilities

### Two Development Modes

The Blazor component library supports two modes to fit different use cases:

| Mode | Use Case | Features |
|------|----------|----------|
| **Standalone** | Simple viewer apps, demos, embedded viewers | Load wexBIM files directly, no backend required, IFC processing in Blazor Server |
| **Platform** | Full BIM applications with model management | Connect to Xbim Server for storage, versioning, collaboration, and cloud processing |

Choose **Standalone** mode when you need a lightweight viewer without server infrastructure. Choose **Platform** mode when building applications that require model persistence, user management, or team collaboration.

## Architecture

```
Xbim/
├── src/
│   ├── Xbim.WexBlazor                          # Blazor component library (NuGet package)
│   ├── Xbim.WexServer.Client                          # Generated API client (NuGet package)
│   ├── Xbim.Web                             # Blazor Server web application
│   ├── Xbim.WexServer.App                      # ASP.NET Core REST API
│   ├── Xbim.WexServer.Domain                   # Domain entities
│   ├── Xbim.WexServer.Contracts                # DTOs and API contracts
│   ├── Xbim.WexServer.Abstractions             # Interfaces and abstractions
│   ├── Xbim.WexServer.Persistence.EfCore       # Entity Framework Core data access
│   ├── Xbim.WexServer.Processing               # Background job processing
│   ├── Xbim.WexServer.Storage.LocalDisk        # Local disk storage provider
│   ├── Xbim.WexServer.Storage.AzureBlob        # Azure Blob storage provider
│   ├── Xbim.ServiceDefaults                 # .NET Aspire shared configuration
│   └── Xbim.AppHost                         # .NET Aspire orchestration
├── samples/
│   ├── Xbim.WexBlazor.Sample                   # WebAssembly standalone demo
│   └── Xbim.WexBlazor.Server.Sample            # Blazor Server standalone demo with IFC support
└── tests/
    └── ...                                     # Unit and integration tests
```

## Features

### Server Features
- **Model Management** - Workspaces, projects, models, and versioning
- **IFC Processing** - Automatic conversion to wexBIM format using xBIM Geometry Engine
- **Property Extraction** - Extract and store IFC properties in database for fast retrieval
- **Multiple Storage Backends** - Local disk or Azure Blob Storage
- **Background Processing** - Async job queue for long-running operations
- **Role-Based Access** - Workspace and project membership with Owner/Member/Viewer roles

### Viewer Features
- **3D BIM Visualization** - WebGL-based viewer using @xbim/viewer
- **Plugin System** - Navigation cube, grid, section box, clipping planes
- **Sidebar Docking** - Dockable/overlay panels for properties and hierarchy
- **Property Display** - Multi-source property aggregation from IFC, database, or custom sources
- **Model Hierarchy** - Product types and spatial structure navigation
- **Theming** - Light/dark themes with customizable accent colors
- **Direct IFC Loading** - Server-side IFC to wexBIM conversion (Blazor Server only)

## Quick Start

### Standalone Mode (No Server)

For simple viewer applications without backend infrastructure:

```bash
# Add GitHub Packages source (one-time setup)
dotnet nuget add source https://nuget.pkg.github.com/Ibrahim5aad/index.json --name github --username YOUR_GITHUB_USERNAME --password YOUR_GITHUB_PAT

# Install the package
dotnet add package Xbim.WexBlazor
```

> **Note:** The `YOUR_GITHUB_PAT` needs `read:packages` scope. [Create a PAT here](https://github.com/settings/tokens).

Register services in `Program.cs`:

```csharp
builder.Services.AddXbimBlazorStandalone();
```

Add to `_Imports.razor`:

```razor
@using Xbim.WexBlazor
@using Xbim.WexBlazor.Components
```

Use the viewer component:

```razor
<XbimViewer Id="myViewer"
               Width="800"
               Height="600"
               ModelUrl="models/SampleModel.wexbim"
               OnModelLoaded="HandleModelLoaded">
    <ViewerToolbar Position="ToolbarPosition.Top" />
    <ViewerSidebar Position="SidebarPosition.Right">
        <SidebarPanel Title="Properties" Icon="bi-info-circle">
            <PropertiesPanel ShowHeader="false" />
        </SidebarPanel>
    </ViewerSidebar>
</XbimViewer>
```

In standalone mode, you can:
- Load wexBIM files from URLs or byte arrays
- Process IFC files directly (Blazor Server only)
- Display properties from IFC models or custom sources
- Use all viewer plugins and UI components

### Platform Mode (With Xbim Server)

For full BIM applications with model management, storage, and collaboration:

```bash
# If you haven't added the GitHub Packages source yet (see Standalone Mode above)
dotnet add package Xbim.WexBlazor
dotnet add package Xbim.WexServer.Client
```

Register services in `Program.cs`:

```csharp
builder.Services.AddXbimClient(options =>
{
    options.BaseUrl = "https://your-Xbim-server.com";
});
builder.Services.AddXbimBlazorPlatform();
```

In platform mode, you additionally get:
- Cloud storage for models (Azure Blob, local disk)
- Model versioning and history
- Workspace and project organization
- User authentication and role-based access
- Server-side IFC processing with job queues
- Property extraction and database storage

### Running the Server

1. Configure the database and storage in `appsettings.json`:

```json
{
  "Database": {
    "Provider": "Sqlite"
  },
  "Storage": {
    "Provider": "LocalDisk",
    "LocalDisk": {
      "BasePath": "Xbim-storage"
    }
  }
}
```

2. Run the server:

```bash
dotnet run --project src/Xbim.WexServer.App
```

### Running with .NET Aspire

For local development with full orchestration:

```bash
dotnet run --project src/Xbim.AppHost
```

## Server Configuration

### Database Providers

**SQLite** (Development):
```json
{
  "Database": {
    "Provider": "Sqlite"
  }
}
```

**SQL Server** (Production):
```json
{
  "Database": {
    "Provider": "SqlServer"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=...;Database=Xbim;..."
  }
}
```

### Storage Providers

**Local Disk**:
```json
{
  "Storage": {
    "Provider": "LocalDisk",
    "LocalDisk": {
      "BasePath": "Xbim-storage"
    }
  }
}
```

**Azure Blob Storage**:
```json
{
  "Storage": {
    "Provider": "AzureBlob",
    "AzureBlob": {
      "ConnectionString": "...",
      "ContainerName": "Xbim-models"
    }
  }
}
```

### Authentication

**Development Mode** (auto-injects test user):
```json
{
  "Auth": {
    "Mode": "Development",
    "Dev": {
      "Subject": "dev-user",
      "Email": "dev@localhost",
      "DisplayName": "Development User"
    }
  }
}
```

**JWT Bearer** (Production):
```json
{
  "Auth": {
    "Mode": "Bearer"
  }
}
```

## API Endpoints

The server exposes RESTful endpoints for:

| Endpoint | Description |
|----------|-------------|
| `/api/v1/workspaces` | Workspace management |
| `/api/v1/projects` | Project management |
| `/api/v1/models` | Model management |
| `/api/v1/models/{id}/versions` | Model versioning |
| `/api/v1/files` | File metadata |
| `/api/v1/files/upload` | File upload |
| `/api/v1/properties` | Element properties |
| `/api/v1/usage` | Storage usage statistics |

Full API documentation available at `/swagger` when running the server.

## Development

### Prerequisites

- .NET 9.0 SDK
- Node.js 20+ (for TypeScript compilation)
- SQL Server or SQLite

### Building

```bash
git clone https://github.com/Ibrahim5aad/Xbim.git
cd Xbim
dotnet build
```

### Running Tests

```bash
dotnet test
```

### Running Samples

**WebAssembly Sample** (standalone viewer):
```bash
dotnet run --project samples/Xbim.WexBlazor.Sample
```

**Blazor Server Sample** (with IFC processing):
```bash
dotnet run --project samples/Xbim.WexBlazor.Server.Sample
```

## Documentation

- [Xbim.WexBlazor Component Library](src/Xbim.WexBlazor/README.md)
- [Xbim.WexServer.Client API Client](src/Xbim.WexServer.Client/README.md)

## Technology Stack

- **Frontend**: Blazor (Server/WebAssembly), @xbim/viewer, TypeScript
- **Backend**: ASP.NET Core 9, Entity Framework Core 9
- **BIM Processing**: xBIM Essentials, xBIM Geometry Engine
- **Storage**: Local Disk, Azure Blob Storage
- **Orchestration**: .NET Aspire
- **Observability**: OpenTelemetry

## License

MIT

## Acknowledgements

- [@xbim/viewer](https://www.npmjs.com/package/@xbim/viewer) - WebGL BIM viewer
- [xBIM Toolkit](https://github.com/xBimTeam) - .NET BIM libraries
