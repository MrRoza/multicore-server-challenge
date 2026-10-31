#!/usr/bin/env bash
# Runs the server in the foreground. Config: DATABASE_URL, JWT_SECRET, HOST, PORT.
set -euo pipefail
cd "$(dirname "$0")/bin"   # appsettings.json is read from the working directory
export DOTNET_ROOT="${DOTNET_ROOT:-/usr/local/dotnet}" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export DB_POOL_SIZE="${DB_POOL_SIZE:-90}"   # the challenge allows 95 connections
DOTNET="$DOTNET_ROOT/dotnet"
[ -x "$DOTNET" ] || DOTNET=dotnet
exec "$DOTNET" conduit.dll
