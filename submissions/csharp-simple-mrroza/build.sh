#!/usr/bin/env bash
# Builds each service -> bin/<service>/ (Release).
set -euo pipefail
cd "$(dirname "$0")"
export DOTNET_ROOT="${DOTNET_ROOT:-/usr/local/dotnet}" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
DOTNET="$DOTNET_ROOT/dotnet"
[ -x "$DOTNET" ] || DOTNET=dotnet
for service in users profiles articles comments tags gateway; do
  project="src/Conduit.${service^}/Conduit.${service^}.csproj"
  "$DOTNET" publish "$project" -c Release -o "bin/$service" -v quiet -nologo
done
