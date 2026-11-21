# KindleHub.NET
Every one of the 83 official arcade games is now playable in-process, scores flow to the shared `kh_scores` leaderboards, and App Store downloads carry the same polyfill runtime the website injects — so a store app behaves identically here and on the web build. (Admin/moderator-specific functions still need admin/mod roles, so they remain out. Also, aran, do NOT fuck with the GPL.)

Cross-platform KindleHub client with a VB.NET core library and Avalonia UI frontend. Desktop builds run on Windows, Linux, and macOS; an Android mobile host is in its initial port phase. (The iOS host was removed.)

## Architecture

- **KindleHub.Core** (VB.NET, .NET 8.0) - Core library with:
  - Authentication (SHA-256 + PBKDF2/AES-GCM compatible with JS `_userKey`/`_khPackAccount`)
  - Supabase REST API client
  - Domain models (UserProfile, Group, Message, LeaderboardEntry, AppInfo, etc.)

- **KindleHub.Client.Shared** (C#, Avalonia UI, .NET 8.0) - Shared views, view models, assets, and dependency setup used by each host.
- **KindleHub.Client** (C#, Avalonia desktop host, .NET 8.0) - Windows, macOS, and Linux launcher with:
  - MVVM architecture
  - Views: Home, Arcade, Leaderboards, Community, Messages, App Store, Settings
  - Dependency injection with Microsoft.Extensions.Hosting
- **The arcade** (`KindleHub.Client.Shared/Games/`) - All 83 official games playable in-process through one shared board view (`IGame`/`GameBase`/`GameCell`): every puzzle, word, card, board, real-time arcade and strategy title in the official `GAME_HELP` catalogue, each registered in `GameRegistry`. Real-time games (Snake, Tetris, Pac-Man, Galaga, …) run off the window's tick clock; everything else is tap/keyboard driven.
- **Leaderboards** - Finishing a game submits `(slug, score)` to the global `kh_scores` board with the website's run-token pipeline (clamp 1..1e9, 30s dedupe, offline queue cap 60). After a submit the status line reports your rank, and the game window's "Top scores" strip shows the top 10 with your row highlighted. Score formulas match the official ones where they are known (FreeCell `2000−moves`, dominoes `50×matches+rounds`, mahjong `10×cleared+cleanWins`, nerdle, wordle, hangman, ttt …).
- **App Store polyfills** - Downloaded/imported apps are wrapped in the same runtime the website seals them with (`AppPolyfills`): the CSP, the e-ink timer throttle, the guard (no forms, no WebRTC, no navigation), scoped `kh_appls_<id>` storage with the 64 KB cap, the canvas screenshot reporter, dialog boxes, a per-launch nonce, and a local bridge that answers the `{kh:"app"}` postMessage protocol (saveLocal / preview / cloudGet / cloudPut / cloudList / sendMail) with the website's reply shapes and error strings.
- **KindleHub.Client.Android** - .NET 10 platform launcher using the shared UI and a touch-sized bottom navigation bar.

## Prerequisites

- **.NET 10.0.100 SDK** or later ([download](https://dotnet.microsoft.com/download)). The app still targets .NET 8; the newer compiler is required by Avalonia 12.1's XAML analyzers.
- **Git** (for cloning)

### Platform-specific notes

| Platform | Notes |
|----------|-------|
| **Windows** | Use PowerShell script (`build.ps1`). Requires Windows 10/11 x64 or ARM64. Untested! |
| **Linux** | Use bash script (`build.sh`). Supports X11 and native Wayland sessions. Tested on Debian 13+ (KDE Plasma 6.7.4, Wayland), EndavourOS (KDE Plasma 6.7.4, Wayland) |
| **macOS** | Use bash script (`build.sh`). Requires macOS 12+ (Monterey). Release builds include separate Intel (`osx-x64`) and Apple Silicon (`osx-arm64`) packages. |
| **Android** | Install the .NET Android workload, Android SDK Platform 36, Android build tools, and JDK 21. Build with `dotnet build KindleHub.Client.Android/KindleHub.Client.Android.csproj`. |

The mobile shell reuses the existing views and view models. Actions that open standalone desktop windows (such as story/game viewers and message action panels), and launching downloaded HTML apps, still need mobile page/dialog or platform-native replacements.

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

GitHub release packages are published as `...-osx-x64.tar.gz` for Intel Macs
and `...-osx-arm64.tar.gz` for Apple Silicon Macs. Use the `osx-x64` RID when
building locally for an Intel Mac. Automated releases also include an
installable Android arm64 APK (`...-android-arm64.apk`).

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

## NOTICE
~~I'm so fucking tired with KindleHub, you know what, this client is discontinued, aran TRULY hates open-source software, privacy, and transparency. The APIs no longer work. They all report being "Forbidden".
Wow aran. You did a perfect job of trying to hide your steps, and it's absolutely perfect for the community to stay dumb about what you did, amirite guys????? "Yo pay me because I need credits for Claude Max-" dude your site looks like you only did one fucking prompt every time you wanted to update anything, and shipped without a SINGLE FORM OF TESTING. And, better yet, that doesn't even look like you used Claude.~~
Guess what. He replied. What did he say? Well...

"ok go on then suprusr(i know u made the evil helper thing on github, and you were also arancool3001 and i also know where you live and don't even try to trick me i spent no time locating you)
also it was opus 5.5 and fable 5.2."

Evil helper thing. Aran I'm so sick with your ass, I decided to extend support for this thing. If you hate transparency and FOSS software, then so be it. More FOSS software with GPL for you. What's wrong? Oh so you wanted to lock everyone to your web build? WELL BAD FUCKING LUCK. Open-source software documenting your shit is here to stay. Don't hide everything aran, you scummy little asshole.
