<# 
.SYNOPSIS
    KindleHub Pro - Cross-platform build script for Windows

.DESCRIPTION
    Builds the KindleHub Pro solution and optionally publishes self-contained executables.

.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Release

.PARAMETER Publish
    Publish the application after building.

.PARAMETER SelfContained
    Publish as self-contained (includes .NET runtime).

.PARAMETER Runtime
    Target runtime identifier (e.g., win-x64, win-arm64). Required for self-contained.
#>

param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    
    [switch]$Publish,
    
    [switch]$SelfContained,
    
    [string]$Runtime
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$coreProject = Join-Path $scriptDir "KindleHub.Core", "KindleHub.Core.vbproj"
$clientProject = Join-Path $scriptDir "KindleHub.Client", "KindleHub.Client.csproj"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "KindleHub Pro Build Script (Windows)" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "Publish: $Publish" -ForegroundColor Cyan
Write-Host "Self-contained: $SelfContained" -ForegroundColor Cyan
if ($Runtime) { Write-Host "Runtime: $Runtime" -ForegroundColor Cyan }
Write-Host "==========================================" -ForegroundColor Cyan

# Check for .NET SDK
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "Error: .NET SDK not found. Install .NET 8.0+ from https://dotnet.microsoft.com/download"
    exit 1
}

$dotnetVersion = (dotnet --version).Split('.')[0]
if ([int]$dotnetVersion -lt 8) {
    Write-Error "Error: .NET 8.0+ required (found $dotnetVersion)"
    exit 1
}

Write-Host "Using .NET $(dotnet --version)" -ForegroundColor Green

# Restore dependencies
Write-Host "Restoring dependencies..." -ForegroundColor Yellow
dotnet restore $clientProject

# Build core library first
Write-Host "Building KindleHub.Core..." -ForegroundColor Yellow
if ($SelfContained -and $Runtime) {
    dotnet build $coreProject --configuration $Configuration --no-restore --runtime $Runtime
} else {
    dotnet build $coreProject --configuration $Configuration --no-restore
}

# Build client (must build from project dir for XAML compilation)
Write-Host "Building KindleHub.Client..." -ForegroundColor Yellow
if ($SelfContained -and $Runtime) {
    Set-Location (Split-Path $clientProject)
    dotnet build "KindleHub.Client.csproj" --configuration $Configuration --no-restore --runtime $Runtime
    Set-Location $scriptDir
} else {
    Set-Location (Split-Path $clientProject)
    dotnet build "KindleHub.Client.csproj" --configuration $Configuration --no-restore
    Set-Location $scriptDir
}

# Publish if requested
if ($Publish) {
    Write-Host "Publishing KindleHub.Client..." -ForegroundColor Yellow
    
    $publishArgs = @(
        "publish", $clientProject,
        "--configuration", $Configuration,
        "--output", Join-Path $scriptDir "artifacts", $Configuration, "windows"
    )
    
    if ($SelfContained) {
        if (-not $Runtime) {
            $Runtime = "win-x64"
        }
        $publishArgs += "--self-contained", "true", "--runtime", $Runtime
        $publishArgs += "-p:PublishTrimmed=true"
        $publishArgs += "-p:PublishSingleFile=true"
    } else {
        $publishArgs += "--no-build"
    }
    
    dotnet @publishArgs
    
    Write-Host ""
    Write-Host "Published to: $(Join-Path $scriptDir "artifacts", $Configuration, "windows")" -ForegroundColor Green
    
    # Create a batch file for convenience
    $runScript = Join-Path $scriptDir "artifacts", $Configuration, "windows", "KindleHub.bat"
    @"
@echo off
"%~dp0KindleHub.Client.exe" %*
"@ | Set-Content $runScript -Encoding ASCII
    Write-Host "Created run script: $runScript" -ForegroundColor Green
}

Write-Host ""
Write-Host "Build completed successfully!" -ForegroundColor Green