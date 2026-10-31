#!/usr/bin/env bash
# Builds the server as a normal user; every cache stays inside this folder.
set -euo pipefail
cd "$(dirname "$0")"
export DOTNET_ROOT=/usr/local/dotnet-bare
export HOME="$PWD/.home"
export DOTNET_CLI_HOME="$HOME"
export NUGET_PACKAGES="$HOME/nuget"
export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
mkdir -p "$HOME"
"$DOTNET_ROOT/dotnet" publish src/Conduit.csproj -c Release -o out --nologo -v quiet
