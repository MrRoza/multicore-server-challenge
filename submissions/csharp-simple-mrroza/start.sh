#!/usr/bin/env bash
# Runs every service in the foreground: the gateway on HOST:PORT, the slices on internal ports.
# Config: DATABASE_URL, JWT_SECRET, HOST, PORT. If one service exits, they all stop.
set -euo pipefail
DIR="$(cd "$(dirname "$0")" && pwd)"
export DOTNET_ROOT="${DOTNET_ROOT:-/usr/local/dotnet}" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
DOTNET="$DOTNET_ROOT/dotnet"
[ -x "$DOTNET" ] || DOTNET=dotnet

trap 'kill $(jobs -p) 2>/dev/null || true' EXIT

# name, internal port, Postgres connections (90 in all: the challenge allows 95)
slice() {
  (cd "$DIR/bin/$1" && HOST=127.0.0.1 PORT="$2" DB_POOL_SIZE="$3" exec "$DOTNET" "conduit-$1.dll") &
}
slice users 13001 10
slice profiles 13002 10
slice articles 13003 45
slice comments 13004 20
slice tags 13005 5

for port in 13001 13002 13003 13004 13005; do
  until (exec 3<>"/dev/tcp/127.0.0.1/$port") 2>/dev/null; do sleep 0.2; done
done

export USERS_URL=http://127.0.0.1:13001 PROFILES_URL=http://127.0.0.1:13002 ARTICLES_URL=http://127.0.0.1:13003 \
  COMMENTS_URL=http://127.0.0.1:13004 TAGS_URL=http://127.0.0.1:13005
(cd "$DIR/bin/gateway" && exec "$DOTNET" conduit-gateway.dll) &

wait -n
