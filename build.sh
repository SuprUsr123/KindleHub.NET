#!/bin/bash
# KindleHub Pro - Cross-platform build script for Linux/macOS
# Usage: ./build.sh [Debug|Release] [--publish] [--self-contained] [--runtime <RID>]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOLUTION_FILE="$SCRIPT_DIR/KindleHub.sln"
CONFIGURATION="${1:-Release}"
PUBLISH=false
SELF_CONTAINED=false
RUNTIME=""

# Parse arguments
shift || true
while [[ $# -gt 0 ]]; do
    case $1 in
        --publish)
            PUBLISH=true
            shift
            ;;
        --self-contained)
            SELF_CONTAINED=true
            shift
            ;;
        --runtime)
            RUNTIME="$2"
            shift 2
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Default runtimes for self-contained builds
if [[ "$SELF_CONTAINED" == "true" && -z "$RUNTIME" ]]; then
    case "$(uname -s)" in
        Linux*)   RUNTIME="linux-x64" ;;
        Darwin*)  RUNTIME="osx-x64" ;;
        *)        echo "Unknown OS for default runtime"; exit 1 ;;
    esac
fi

echo "=========================================="
echo "KindleHub Pro Build Script"
echo "Configuration: $CONFIGURATION"
echo "Publish: $PUBLISH"
echo "Self-contained: $SELF_CONTAINED"
[[ -n "$RUNTIME" ]] && echo "Runtime: $RUNTIME"
echo "=========================================="

# Avalonia 12's XAML source generators require the newer Roslyn compiler
# shipped with .NET SDK 9.0.300 or later. Prefer the user-local SDK when the
# system dotnet is older (common on Linux distributions).
if ! command -v dotnet &> /dev/null; then
    echo "Error: .NET SDK 9.0.300+ not found. Install it from https://dotnet.microsoft.com/download"
    exit 1
fi

DOTNET_CMD="$(command -v dotnet)"
DOTNET_VERSION="$(dotnet --version 2>/dev/null || true)"
sdk_is_new_enough() {
    local sdk_version="$1"
    [[ "$sdk_version" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+) ]] || return 1
    local sdk_major="${BASH_REMATCH[1]}"
    local sdk_minor="${BASH_REMATCH[2]}"
    local sdk_patch="${BASH_REMATCH[3]}"
    (( sdk_major > 9 || (sdk_major == 9 && (sdk_minor > 0 || (sdk_minor == 0 && sdk_patch >= 300))) ))
}
if [[ -x "$HOME/.dotnet/dotnet" ]]; then
    USER_DOTNET_VERSION="$("$HOME/.dotnet/dotnet" --version 2>/dev/null || true)"
    if sdk_is_new_enough "$USER_DOTNET_VERSION"; then
        DOTNET_CMD="$HOME/.dotnet/dotnet"
        DOTNET_VERSION="$USER_DOTNET_VERSION"
    fi
fi

if ! sdk_is_new_enough "$DOTNET_VERSION"; then
    echo "Error: Avalonia 12 requires .NET SDK 9.0.300 or later (found ${DOTNET_VERSION:-none})."
    echo "Install a newer SDK or put it earlier in PATH."
    exit 1
fi

echo "Using .NET $DOTNET_VERSION ($DOTNET_CMD)"

# Restore dependencies
echo "Restoring dependencies..."
"$DOTNET_CMD" restore "$SOLUTION_FILE"

# Build core library first
echo "Building KindleHub.Core..."
"$DOTNET_CMD" build "$SCRIPT_DIR/KindleHub.Core/KindleHub.Core.vbproj" --configuration "$CONFIGURATION" --no-restore

# Build client (must build from project dir for XAML compilation)
echo "Building KindleHub.Client..."
(cd "$SCRIPT_DIR/KindleHub.Client" && "$DOTNET_CMD" build "KindleHub.Client.csproj" --configuration "$CONFIGURATION" --no-restore)

# Publish if requested
if [[ "$PUBLISH" == "true" ]]; then
    echo "Publishing KindleHub.Client..."
    
    PUBLISH_ARGS=(
        "publish" "KindleHub.Client.csproj"
        "--configuration" "$CONFIGURATION"
        "--output" "$SCRIPT_DIR/artifacts/$CONFIGURATION/$(uname -s | tr '[:upper:]' '[:lower:]')"
    )
    
    if [[ "$SELF_CONTAINED" == "true" ]]; then
        PUBLISH_ARGS+=("--self-contained" "true" "--runtime" "$RUNTIME")
        # Only trim on Windows for single-file
        if [[ "$RUNTIME" == "win-*" ]]; then
            PUBLISH_ARGS+=("-p:PublishTrimmed=true" "-p:PublishSingleFile=true")
        fi
    else
        PUBLISH_ARGS+=("--no-build")
    fi
    
    (cd "$SCRIPT_DIR/KindleHub.Client" && "$DOTNET_CMD" "${PUBLISH_ARGS[@]}")
    
    echo ""
    echo "Published to: $SCRIPT_DIR/artifacts/$CONFIGURATION/$(uname -s | tr '[:upper:]' '[:lower:]')"
    
    # Create a run script for convenience
    RUN_SCRIPT="$SCRIPT_DIR/artifacts/$CONFIGURATION/$(uname -s | tr '[:upper:]' '[:lower:]')/KindleHub"
    cat > "$RUN_SCRIPT" << 'EOF'
#!/bin/bash
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec "$DIR/KindleHub.Client" "$@"
EOF
    chmod +x "$RUN_SCRIPT"
    echo "Created run script: $RUN_SCRIPT"
fi

echo ""
echo "Build completed successfully!"
