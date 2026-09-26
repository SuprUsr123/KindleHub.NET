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

# Check for .NET SDK
if ! command -v dotnet &> /dev/null; then
    echo "Error: .NET SDK not found. Install .NET 8.0+ from https://dotnet.microsoft.com/download"
    exit 1
fi

DOTNET_VERSION=$(dotnet --version | cut -d. -f1)
if [[ "$DOTNET_VERSION" -lt 8 ]]; then
    echo "Error: .NET 8.0+ required (found $DOTNET_VERSION)"
    exit 1
fi

echo "Using .NET $(dotnet --version)"

# Restore dependencies
echo "Restoring dependencies..."
dotnet restore "$SOLUTION_FILE"

# Build core library first
echo "Building KindleHub.Core..."
dotnet build "$SCRIPT_DIR/KindleHub.Core/KindleHub.Core.vbproj" --configuration "$CONFIGURATION" --no-restore

# Build client (must build from project dir for XAML compilation)
echo "Building KindleHub.Client..."
(cd "$SCRIPT_DIR/KindleHub.Client" && dotnet build "KindleHub.Client.csproj" --configuration "$CONFIGURATION" --no-restore)

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
    
    (cd "$SCRIPT_DIR/KindleHub.Client" && dotnet "${PUBLISH_ARGS[@]}")
    
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