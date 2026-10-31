#!/usr/bin/env bash
# Runs what CI runs, in Docker, so it needs nothing else installed: on a clean Ubuntu 24.04 with Postgres 18, it seeds the
# dev database, then runs install.sh and the conformance suite (test/run.sh) against this folder as it is on disk.
set -euo pipefail
export MSYS_NO_PATHCONV=1
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
NAME="$(basename "$HERE")"
GO_VERSION="$(awk '/^go /{print $2}' "$REPO/bench/go.mod")"
DB=conduit-run-postgres
BOX=conduit-run-box

cleanup() { docker rm -f "$BOX" "$DB" >/dev/null 2>&1 || true; }
trap cleanup EXIT
cleanup

docker run -d --name "$DB" -e POSTGRES_PASSWORD=postgres postgres:18 >/dev/null
docker run -d --name "$BOX" --network "container:$DB" ubuntu:24.04 sleep infinity >/dev/null
docker exec "$BOX" mkdir -p /work

cd "$REPO"
git -c core.autocrlf=false archive HEAD | docker exec -i "$BOX" tar -x -C /work
docker exec "$BOX" rm -rf "/work/submissions/$NAME"
tar --exclude=bin --exclude=obj -c "submissions/$NAME" | docker exec -i "$BOX" tar -x -C /work

docker exec -i -e GO_VERSION="$GO_VERSION" -e NAME="$NAME" "$BOX" bash -s <<'SCRIPT'
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive PGHOST=127.0.0.1 PGPORT=5432 PGUSER=postgres PGPASSWORD=postgres
apt-get update -qq >/dev/null
apt-get install -y -qq postgresql-client curl jq ca-certificates tar >/dev/null 2>&1
curl -fsSL "https://go.dev/dl/go$GO_VERSION.linux-amd64.tar.gz" | tar -C /usr/local -xz
export PATH=/usr/local/go/bin:$PATH
cd /work
until pg_isready -q; do sleep 1; done
echo "== seed"
bash seed/make-seed.sh --dev | tail -1
echo "== install.sh"
bash "submissions/$NAME/install.sh" >/dev/null 2>&1
bash test/run.sh "submissions/$NAME"
SCRIPT
