# KindleHub.NET
95% completed! Most functions are wired up now. Also, aran, do NOT fuck with the GPL.

Cross-platform KindleHub client with a VB.NET core library and Avalonia UI frontend. Runs on Windows, Linux, and macOS.

## Architecture

- **KindleHub.Core** (VB.NET, .NET 8.0) - Core library with:
  - Authentication (SHA-256 + PBKDF2/AES-GCM compatible with JS `_userKey`/`_khPackAccount`)
  - Supabase REST API client
  - Domain models (UserProfile, Group, Message, LeaderboardEntry, AppInfo, etc.)

- **KindleHub.Client** (C#, Avalonia UI, .NET 8.0) - Cross-platform UI with:
  - MVVM architecture
  - Views: Home, Leaderboards, Community, Messages, App Store, Settings
  - Dependency injection with Microsoft.Extensions.Hosting

## Prerequisites

- **.NET 10.0.100 SDK** or later ([download](https://dotnet.microsoft.com/download)). The app still targets .NET 8; the newer compiler is required by Avalonia 12.1's XAML analyzers.
- **Git** (for cloning)

### Platform-specific notes

| Platform | Notes |
|----------|-------|
| **Windows** | Use PowerShell script (`build.ps1`). Requires Windows 10/11 x64 or ARM64. Untested! |
| **Linux** | Use bash script (`build.sh`). Supports X11 and native Wayland sessions. Tested on Debian 13+ (KDE Plasma 6.7.4, Wayland), EndavourOS (KDE Plasma 6.7.4, Wayland) |
| **macOS** | Use bash script (`build.sh`). Requires macOS 12+ (Monterey). Works on Intel and Apple Silicon (probably). Untested! |

## Quick Start

### Linux / macOS

```bash
# Clone and navigate
cd KindleHubClient

# Make script executable (first time only)
chmod +x build.sh

# Build (Release configuration)
./build.sh

# Build and publish self-contained
./build.sh Release --publish --self-contained

# Run published app
./artifacts/Release/linux/KindleHub    # Linux
./artifacts/Release/darwin/KindleHub   # macOS
```

### Windows (PowerShell)

```powershell
# Navigate to project
cd KindleHubClient

# Build (Release configuration)
.\build.ps1

# Build and publish self-contained
.\build.ps1 -Configuration Release -Publish -SelfContained -Runtime win-x64

# Run published app
.\artifacts\Release\windows\KindleHub.bat
```

## Build Script Options

### Linux/macOS (`build.sh`)

```bash
./build.sh [Configuration] [Options]

Configuration:  Debug | Release (default: Release)

Options:
  --publish          Publish the application after building
  --self-contained   Include .NET runtime (no .NET install required on target)
  --runtime <RID>    Target runtime identifier (default: auto-detect)
```

**Common Runtime Identifiers (RIDs):**
- `linux-x64`, `linux-arm64`
- `osx-x64`, `osx-arm64`
- `win-x64`, `win-arm64`

### Windows (`build.ps1`)

```powershell
.\build.ps1 [-Configuration <Debug|Release>] [-Publish] [-SelfContained] [-Runtime <RID>]

Parameters:
  -Configuration    Debug | Release (default: Release)
  -Publish          Publish after building
  -SelfContained    Include .NET runtime
  -Runtime          Target RID (required for -SelfContained, default: win-x64)
```

## Development Build

For development with hot reload:

```bash
# Linux/macOS
cd KindleHubClient/KindleHub.Client
dotnet watch run

# Windows (PowerShell)
cd KindleHubClient/KindleHub.Client
dotnet watch run
```

## Project Structure

```
KindleHubClient/
├── KindleHub.sln                 # Solution file
├── build.sh                      # Linux/macOS build script
├── build.ps1                     # Windows build script
├── KindleHub.Core/               # VB.NET core library
│   ├── KindleHub.Core.vbproj
│   ├── Api/                      # Supabase REST API client
│   ├── Crypto/                   # Encryption (PBKDF2, AES-GCM)
│   ├── Models/                   # Domain models
│   └── Services/                 # Core services
└── KindleHub.Client/             # Avalonia UI frontend
    ├── KindleHub.Client.csproj
    ├── ViewModels/               # MVVM ViewModels
    ├── Views/                    # XAML Views
    ├── Services/                 # UI services
    ├── Program.cs                # Entry point + DI setup
    └── App.axaml                 # Application resources
```

## Output Locations

| Build Type | Location |
|------------|----------|
| Standard build | `KindleHub.Core/bin/<Config>/net8.0/`, `KindleHub.Client/bin/<Config>/net8.0/` |
| Published (Linux) | `artifacts/<Config>/linux/` |
| Published (macOS) | `artifacts/<Config>/darwin/` |
| Published (Windows) | `artifacts/<Config>/windows/` |

## Troubleshooting

### "GTK not found" on Linux
```bash
# Ubuntu/Debian
sudo apt install libgtk-3-0

# Fedora
sudo dnf install gtk3

# Arch
sudo pacman -S gtk3
```

### "Avalonia XAML compilation errors"
- Ensure `EnableAvaloniaXamlCompilation=true` in `.csproj`
- Avoid `x:DataType` in XAML (uses compiled bindings without it)
- Use `PlaceholderText` instead of `Watermark` on TextBox

### Build fails with "namespace not found" (VB.NET)
- Remove explicit `Namespace` blocks from `.vb` files
- Use `RootNamespace` in `.vbproj` instead
- Ensure `Imports System.Threading` and `Imports System.Collections.Generic` are present

### Self-contained publish is large
- **Windows**: Enable trimming (`-p:PublishTrimmed=true`) and single-file (`-p:PublishSingleFile=true`) - default in build script
- **Linux/macOS**: Trimming disabled by default (breaks Avalonia XAML); runtime is included but not trimmed

## API Configuration

The client connects to: `https://kindlehub-api.arancool3000.workers.dev` (Supabase `/rest/v1/`)

To use a different backend, modify `KindleHub.Core/Api/KindleHubApiClient.vb`:
```vb
Private Const BaseUrl As String = "https://your-api-endpoint.workers.dev/rest/v1/"
```
See how to host your own instance (still legacy compared to the latest version due to owner taking down repo after implementing Stripe payments in the jankiest way possible) [here](https://github.com/SuprUsr123/KindleHub-Pro-Full-Archive)
## License

GPL License - see LICENSE file for details. (why does the agent keep messing this up, why was it originally MIT)
