#!/usr/bin/env bash
# Runs CI's checks in Docker, needing nothing else on the host: a clean Ubuntu 24.04 next to Postgres 18 with SCRAM
# logins, the dev seed, install.sh, then test/run.sh on this folder as it is on disk. Every container is limited, and
# only ever named bare-*.
#
#   bash run.sh            everything from scratch, then remove the containers
#   bash run.sh up         the containers, seeded and installed, left running
#   bash run.sh build      copy this folder in and build it, showing only errors and warnings
#   bash run.sh test       copy this folder in and run the conformance suite
#   bash run.sh load N     60 s load test with N users (dev seed, -think-scale 0.1)
#   bash run.sh down       remove the containers
#
# CPUS picks the cores (default 16-21).
set -euo pipefail
export MSYS_NO_PATHCONV=1
HERE="$(cd "$(dirname "$0")" && { pwd -W 2>/dev/null || pwd; })"   # pwd -W: a path docker.exe understands on Windows
REPO="$HERE/../.."
NAME="$(basename "$HERE")"
GO_VERSION="$(awk '/^go /{print $2}' "$REPO/bench/go.mod")"
DB=bare-pg
BOX=bare-box
CPUS="${CPUS:-16-21}"
LIMITS=(--cpuset-cpus="$CPUS" --memory=12g --memory-swap=12g)

down() { docker rm -f "$BOX" "$DB" >/dev/null 2>&1 || true; }

copy_in() {
  docker exec "$BOX" rm -rf "/work/submissions/$NAME/src" "/work/submissions/$NAME/README.md"
  docker exec "$BOX" mkdir -p "/work/submissions/$NAME"
  for f in .gitignore README.md build.sh global.json install.sh run.sh start.sh src; do
    docker cp -q "$HERE/$f" "$BOX:/work/submissions/$NAME/"
  done
}

up() {
  down
  # Box Postgres settings at this memory limit: pgtune's "web" values for the benchmark box where they apply.
  docker run -d --name "$DB" "${LIMITS[@]}" --ulimit nofile=200000:200000 \
    -e POSTGRES_PASSWORD=postgres -e POSTGRES_INITDB_ARGS=--auth-host=scram-sha-256 postgres:18 \
    -c max_connections=100 -c shared_buffers=2GB -c effective_cache_size=6GB -c random_page_cost=1.1 \
    -c effective_io_concurrency=200 -c wal_buffers=16MB -c work_mem=6MB -c max_wal_size=4GB -c min_wal_size=1GB \
    -c checkpoint_completion_target=0.9 >/dev/null
  docker run -d --name "$BOX" "${LIMITS[@]}" --ulimit nofile=200000:200000 --network "container:$DB" \
    ubuntu:24.04 sleep infinity >/dev/null
  docker exec "$BOX" mkdir -p /work
  (cd "$REPO" && git -c core.autocrlf=false archive HEAD -- bench box schema.sql seed test | docker exec -i "$BOX" tar -x -C /work)
  copy_in
  docker exec -i -e GO_VERSION="$GO_VERSION" -e NAME="$NAME" "$BOX" bash -s <<'SCRIPT'
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive PGHOST=127.0.0.1 PGPORT=5432 PGUSER=postgres PGPASSWORD=postgres
apt-get update -qq >/dev/null
apt-get install -y -qq postgresql-client curl jq ca-certificates tar procps >/dev/null 2>&1
curl -fsSL "https://go.dev/dl/go$GO_VERSION.linux-amd64.tar.gz" | tar -C /usr/local -xz
export PATH=/usr/local/go/bin:$PATH
cd /work
until pg_isready -q; do sleep 1; done
sleep 2
until pg_isready -q; do sleep 1; done
echo "== seed"
bash seed/make-seed.sh --dev | tail -1
echo "== install.sh"
bash "submissions/$NAME/install.sh" >/dev/null 2>&1
echo "installed"
SCRIPT
}

conformance() {
  copy_in
  docker exec -e NAME="$NAME" "$BOX" bash -c '
    export PATH=/usr/local/go/bin:$PATH PGHOST=127.0.0.1 PGPORT=5432 PGUSER=postgres PGPASSWORD=postgres
    cd /work && bash test/run.sh "submissions/$NAME"'
}

load() {
  copy_in
  docker exec -e NAME="$NAME" -e USERS="${1:-5000}" "$BOX" bash -c '
    set -euo pipefail
    export PATH=/usr/local/go/bin:$PATH PGHOST=127.0.0.1 PGPORT=5432 PGUSER=postgres PGPASSWORD=postgres
    cd /work
    bash "submissions/$NAME/build.sh"
    psql -X -q -d postgres -c "DROP DATABASE IF EXISTS conduit_lg WITH (FORCE)" -c "CREATE DATABASE conduit_lg TEMPLATE conduit_dev STRATEGY FILE_COPY"
    (cd bench && go build -o /tmp/loadgen ./cmd/loadgen)
    DATABASE_URL=postgres://conduit:conduit@127.0.0.1:5432/conduit_lg JWT_SECRET=load-secret HOST=127.0.0.1 PORT=3110 \
      bash "submissions/$NAME/start.sh" >/tmp/server.log 2>&1 &
    SRV=$!
    trap "kill $SRV 2>/dev/null || true" EXIT
    for _ in $(seq 60); do curl -sf -o /dev/null http://127.0.0.1:3110/api/tags && break; sleep 1; done
    /tmp/loadgen run -target http://127.0.0.1:3110 -users "$USERS" -ramp 10s -measure 60s -tail 5s \
      -secret load-secret -articles 10000 -think-scale 0.1 -run-id "bare-$USERS-$(date +%s)" -out /tmp/load.json
    echo "== server log"; tail -5 /tmp/server.log
    echo "== postgres connections"
    psql -X -At -d postgres -c "SELECT count(*) FROM pg_stat_activity WHERE datname = '"'"'conduit_lg'"'"'"
  '
}

build() {
  copy_in
  docker exec -e NAME="$NAME" "$BOX" bash -c '
    cd /work
    bash "submissions/$NAME/build.sh" 2>&1 | grep -E "error|warning CS|warning IL" | sed "s#/work/submissions/$NAME/src/##; s# \[.*##" | sort -u | head -60
    ls -la "submissions/$NAME/out/conduit"'
}

case "${1:-all}" in
  up) up ;;
  build) build ;;
  test) conformance ;;
  load) load "${2:-5000}" ;;
  down) down ;;
  all) trap down EXIT; up; conformance ;;
  *) echo "usage: run.sh [up|test|load N|down]"; exit 2 ;;
esac
