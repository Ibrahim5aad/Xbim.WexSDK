# Octopus

[![Build Status](https://github.com/Ibrahim5aad/Octopus/actions/workflows/ci.yml/badge.svg)](https://github.com/Ibrahim5aad/Octopus/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

An open-source SDK and scaffold for building BIM (Building Information Modeling) applications with .NET 9. Octopus provides reusable components, a REST API server, and client libraries to accelerate the development of custom BIM solutions.

| Package | NuGet |
|---------|-------|
| Octopus.Blazor | [![NuGet](https://img.shields.io/nuget/v/Octopus.Blazor.svg?style=flat-square)](https://www.nuget.org/packages/Octopus.Blazor/) |
| Octopus.Api.Client | [![NuGet](https://img.shields.io/nuget/v/Octopus.Api.Client.svg?style=flat-square)](https://www.nuget.org/packages/Octopus.Api.Client/) |

![Octopus Viewer](screenshot.png)

## What is Octopus?

Octopus is a toolkit for developers building BIM applications. It provides:

- **Blazor Component Library** - Drop-in 3D viewer components for visualizing IFC/wexBIM models
- **REST API Server** - Ready-to-deploy backend for model storage, processing, and management
- **Generated API Client** - Typed HTTP client for seamless server integration
- **Reference Implementation** - Full web application demonstrating all capabilities

### Two Development Modes

The Blazor component library supports two modes to fit different use cases:

| Mode | Use Case | Features |
|------|----------|----------|
| **Standalone** | Simple viewer apps, demos, embedded viewers | Load wexBIM files directly, no backend required, IFC processing in Blazor Server |
| **Platform** | Full BIM applications with model management | Connect to Octopus Server for storage, versioning, collaboration, and cloud processing |

Choose **Standalone** mode when you need a lightweight viewer without server infrastructure. Choose **Platform** mode when building applications that require model persistence, user management, or team collaboration.

## Architecture

```
Octopus/
├── src/
│   ├── Octopus.Blazor                          # Blazor component library (NuGet package)
│   ├── Octopus.Api.Client                          # Generated API client (NuGet package)
│   ├── Octopus.Web                             # Blazor Server web application
│   ├── Octopus.Server.App                      # ASP.NET Core REST API
│   ├── Octopus.Server.Domain                   # Domain entities
│   ├── Octopus.Server.Contracts                # DTOs and API contracts
│   ├── Octopus.Server.Abstractions             # Interfaces and abstractions
│   ├── Octopus.Server.Persistence.EfCore       # Entity Framework Core data access
│   ├── Octopus.Server.Processing               # Background job processing
│   ├── Octopus.Server.Storage.LocalDisk        # Local disk storage provider
│   ├── Octopus.Server.Storage.AzureBlob        # Azure Blob storage provider
│   ├── Octopus.ServiceDefaults                 # .NET Aspire shared configuration
│   └── Octopus.AppHost                         # .NET Aspire orchestration
├── samples/
│   ├── Octopus.Blazor.Sample                   # WebAssembly standalone demo
│   └── Octopus.Blazor.Server.Sample            # Blazor Server standalone demo with IFC support
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
dotnet add package Octopus.Blazor
```

Register services in `Program.cs`:

```csharp
builder.Services.AddOctopusBlazorStandalone();
```

Add to `_Imports.razor`:

```razor
@using Octopus.Blazor
@using Octopus.Blazor.Components
```

Use the viewer component:

```razor
<OctopusViewer Id="myViewer"
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
</OctopusViewer>
```

In standalone mode, you can:
- Load wexBIM files from URLs or byte arrays
- Process IFC files directly (Blazor Server only)
- Display properties from IFC models or custom sources
- Use all viewer plugins and UI components

### Platform Mode (With Octopus Server)

For full BIM applications with model management, storage, and collaboration:

```bash
dotnet add package Octopus.Blazor
dotnet add package Octopus.Api.Client
```

Register services in `Program.cs`:

```csharp
builder.Services.AddOctopusClient(options =>
{
    options.BaseUrl = "https://your-octopus-server.com";
});
builder.Services.AddOctopusBlazorPlatform();
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
      "BasePath": "octopus-storage"
    }
  }
}
```

2. Run the server:

```bash
dotnet run --project src/Octopus.Server.App
```

### Running with .NET Aspire

For local development with full orchestration:

```bash
dotnet run --project src/Octopus.AppHost
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
    "DefaultConnection": "Server=...;Database=Octopus;..."
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
      "BasePath": "octopus-storage"
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
      "ContainerName": "octopus-models"
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
git clone https://github.com/Ibrahim5aad/Octopus.git
cd Octopus
dotnet build
```

### Running Tests

```bash
dotnet test
```

### Running Samples

**WebAssembly Sample** (standalone viewer):
```bash
dotnet run --project samples/Octopus.Blazor.Sample
```

**Blazor Server Sample** (with IFC processing):
```bash
dotnet run --project samples/Octopus.Blazor.Server.Sample
```

## Documentation

- [Octopus.Blazor Component Library](src/Octopus.Blazor/README.md)
- [Octopus.Api.Client API Client](src/Octopus.Api.Client/README.md)

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
