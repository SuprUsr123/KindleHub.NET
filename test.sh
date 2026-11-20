#!/usr/bin/env bash
# Deterministic tests by default. KINDLEHUB_LIVE_TESTS=1 adds public read checks.
set -euo pipefail
cd "$(dirname "$0")"
kindlehub_dotnet="${DOTNET_CMD:-dotnet}"
if [[ -x "$HOME/.dotnet/dotnet" && -z "${DOTNET_CMD:-}" ]]; then
    kindlehub_dotnet="$HOME/.dotnet/dotnet"
fi
"$kindlehub_dotnet" test KindleHub.Tests/KindleHub.Tests.csproj --configuration Release
"$kindlehub_dotnet" test KindleHub.UiTests/KindleHub.UiTests.csproj --configuration Release
"$kindlehub_dotnet" run --project KindleHub.Client.Gametest --configuration Release
