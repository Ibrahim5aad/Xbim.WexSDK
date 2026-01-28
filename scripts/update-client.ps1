#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Updates the Octopus.Api.Client by fetching the latest OpenAPI spec from the server.

.DESCRIPTION
    This script:
    1. Starts the Octopus.Server.App
    2. Waits for the server to be ready
    3. Downloads the swagger.json
    4. Stops the server
    5. Rebuilds the client to regenerate code

.PARAMETER ServerUrl
    The base URL of the server. Default: http://localhost:5100

.PARAMETER SkipServerStart
    If set, assumes the server is already running and skips start/stop.

.EXAMPLE
    ./scripts/update-client.ps1

.EXAMPLE
    ./scripts/update-client.ps1 -SkipServerStart -ServerUrl "http://localhost:5001"
#>

param(
    [string]$ServerUrl = "http://localhost:5100",
    [switch]$SkipServerStart
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$serverProject = Join-Path $repoRoot "src/Octopus.Server.App/Octopus.Server.App.csproj"
$clientDir = Join-Path $repoRoot "src/Octopus.Api.Client"
$swaggerPath = Join-Path $clientDir "swagger.json"

Write-Host "== Octopus Client Update ==" -ForegroundColor Cyan

$serverProcess = $null

try {
    if (-not $SkipServerStart) {
        Write-Host "Starting server..." -ForegroundColor Yellow

        $serverProcess = Start-Process -FilePath "dotnet" `
            -ArgumentList "run", "--project", $serverProject, "--urls", $ServerUrl `
            -PassThru -NoNewWindow

        Write-Host "Waiting for server to be ready..."
        $maxRetries = 30
        $retryCount = 0
        $ready = $false

        while (-not $ready -and $retryCount -lt $maxRetries) {
            Start-Sleep -Seconds 1
            $retryCount++
            try {
                $response = Invoke-WebRequest -Uri "$ServerUrl/healthz" -UseBasicParsing -TimeoutSec 2
                if ($response.StatusCode -eq 200) {
                    $ready = $true
                    Write-Host "Server is ready!" -ForegroundColor Green
                }
            } catch {
                Write-Host "." -NoNewline
            }
        }

        if (-not $ready) {
            throw "Server failed to start within $maxRetries seconds"
        }
    }

    Write-Host "Downloading swagger.json from $ServerUrl/swagger/v1/swagger.json" -ForegroundColor Yellow
    Invoke-WebRequest -Uri "$ServerUrl/swagger/v1/swagger.json" -OutFile $swaggerPath -UseBasicParsing
    Write-Host "Downloaded swagger.json" -ForegroundColor Green

} finally {
    if ($serverProcess -and -not $serverProcess.HasExited) {
        Write-Host "Stopping server..." -ForegroundColor Yellow
        Stop-Process -Id $serverProcess.Id -Force
        Write-Host "Server stopped" -ForegroundColor Green
    }
}

Write-Host "Rebuilding Octopus.Api.Client to regenerate code..." -ForegroundColor Yellow
Push-Location $clientDir
try {
    # Force regeneration by touching swagger.json or cleaning
    dotnet build --no-incremental
} finally {
    Pop-Location
}

Write-Host "== Client updated successfully! ==" -ForegroundColor Cyan
Write-Host "Generated file: src/Octopus.Api.Client/Generated/OctopusClient.cs"
