#!/usr/bin/env bash
# Builds each process -> bin/<service>/ (Release).
set -euo pipefail
cd "$(dirname "$0")"
export DOTNET_ROOT="${DOTNET_ROOT:-/usr/local/dotnet}" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
DOTNET="$DOTNET_ROOT/dotnet"
[ -x "$DOTNET" ] || DOTNET=dotnet
"$DOTNET" publish src/Conduit.Gateway/Conduit.Gateway.csproj -c Release -o bin/gateway -v quiet -nologo
for slice in Users Profiles Articles Comments Tags; do
  "$DOTNET" publish "src/$slice/Conduit.$slice.Api/Conduit.$slice.Api.csproj" -c Release -o "bin/${slice,,}" -v quiet -nologo
done
