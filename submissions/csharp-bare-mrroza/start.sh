#!/usr/bin/env bash
# Runs the server in the foreground. DATABASE_URL, JWT_SECRET, HOST and PORT come from the environment.
set -euo pipefail
cd "$(dirname "$0")"
ulimit -n "$(ulimit -Hn)" 2>/dev/null || true
exec ./out/conduit
