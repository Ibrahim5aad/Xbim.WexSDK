# Xbim.WexServer.Client

[![GitHub Packages](https://img.shields.io/badge/GitHub%20Packages-Xbim.WexServer.Client-blue)](https://github.com/Ibrahim5aad/Xbim/pkgs/nuget/Xbim.WexServer.Client)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A typed HTTP client for the Xbim Server API. Use this package to connect your BIM applications to Xbim Server for model storage, processing, and management. Auto-generated from OpenAPI/Swagger specifications using NSwag.

This package is required when using [Xbim.WexBlazor](https://github.com/Ibrahim5aad/Xbim/pkgs/nuget/Xbim.WexBlazor) in **Platform mode** (connected to Xbim Server). For standalone viewer applications without a backend, you only need Xbim.WexBlazor.

## Features

- **Typed API Client** - Full IntelliSense support for all endpoints
- **Authentication Support** - Pluggable token provider for JWT authentication
- **Resilience** - Built-in retry policies and circuit breakers via Microsoft.Extensions.Http.Resilience
- **HttpClientFactory** - Proper HTTP client lifecycle management

## Installation

```bash
# Add GitHub Packages source (one-time setup)
dotnet nuget add source https://nuget.pkg.github.com/Ibrahim5aad/index.json --name github --username YOUR_GITHUB_USERNAME --password YOUR_GITHUB_PAT

# Install the package
dotnet add package Xbim.WexServer.Client
```

> **Note:** The `YOUR_GITHUB_PAT` needs `read:packages` scope. [Create a PAT here](https://github.com/settings/tokens).

## Quick Start

### Basic Setup

```csharp
// Program.cs
builder.Services.AddXbimClient(options =>
{
    options.BaseUrl = "https://your-Xbim-server.com";
});
```

### With Authentication

Implement `IAuthTokenProvider` for your authentication flow:

```csharp
public class MyTokenProvider : IAuthTokenProvider
{
    public Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        // Return your JWT token
        return Task.FromResult<string?>("your-jwt-token");
    }
}
```

Register with DI:

```csharp
builder.Services.AddSingleton<IAuthTokenProvider, MyTokenProvider>();
builder.Services.AddXbimClient(options =>
{
    options.BaseUrl = "https://your-Xbim-server.com";
});
```

### Using the Client

```csharp
@inject IXbimClient Client

// List workspaces
var workspaces = await Client.WorkspacesAllAsync();

// Create a project
var project = await Client.ProjectsPOSTAsync(new CreateProjectRequest
{
    Name = "My Project",
    WorkspaceId = workspaceId
});

// Upload a model
using var stream = File.OpenRead("model.ifc");
var file = await Client.UploadAsync(new FileParameter(stream, "model.ifc"));

// Get element properties
var properties = await Client.PropertiesAsync(modelVersionId, elementId);
```

## API Endpoints

The client provides typed methods for all server endpoints:

| Method | Description |
|--------|-------------|
| `WorkspacesAllAsync()` | List all workspaces |
| `WorkspacesPOSTAsync(request)` | Create workspace |
| `WorkspacesGETAsync(id)` | Get workspace by ID |
| `ProjectsAllAsync()` | List all projects |
| `ProjectsPOSTAsync(request)` | Create project |
| `ModelsAllAsync()` | List all models |
| `ModelsPOSTAsync(request)` | Create model |
| `VersionsAllAsync(modelId)` | List model versions |
| `VersionsPOSTAsync(modelId, request)` | Create version |
| `UploadAsync(file)` | Upload file |
| `PropertiesAsync(versionId, elementId)` | Get element properties |
| `UsageAsync(workspaceId)` | Get storage usage |

## Configuration Options

```csharp
builder.Services.AddXbimClient(options =>
{
    // Required: Server base URL
    options.BaseUrl = "https://api.example.com";

    // Optional: Request timeout (default: 30s)
    options.Timeout = TimeSpan.FromMinutes(2);
});
```

## Manual Client Creation

For scenarios without DI:

```csharp
var httpClient = new HttpClient
{
    BaseAddress = new Uri("https://your-server.com")
};

var client = new XbimClient(httpClient);
var workspaces = await client.WorkspacesAllAsync();
```

## Regenerating the Client

If the server API changes, regenerate the client:

```bash
# Fetch latest swagger.json from running server
dotnet msbuild -t:FetchSwagger -p:ServerUrl=http://localhost:5000

# Regenerate client code
dotnet msbuild -t:RegenerateClient

# Or combined
dotnet msbuild -t:UpdateClient -p:ServerUrl=http://localhost:5000
```

## Requirements

- .NET 9.0+
- Xbim Server instance

## Dependencies

- `Newtonsoft.Json` - JSON serialization
- `Microsoft.Extensions.Http` - HttpClientFactory
- `Microsoft.Extensions.Http.Resilience` - Retry policies

## License

MIT

## Related Packages

- [Xbim.WexBlazor](https://github.com/Ibrahim5aad/Xbim/pkgs/nuget/Xbim.WexBlazor) - Blazor component library for 3D BIM visualization
