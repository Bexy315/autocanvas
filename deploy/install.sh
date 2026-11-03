#!/bin/sh
# Installs AutoCanvas with docker compose: the server with its web UI and a runner, in a directory
# of its own. Run it again on the same directory to update; its .env is kept.
#
#   deploy/install.sh [--dir ./autocanvas] [--postgres] [--port 5080] [--runner-port 5081]
#
# Until the first release the images are built from this checkout. docs/user/installation.md
# explains every step, also for installing without the script.
set -eu

dir=./autocanvas
postgres=false
port=5080
runner_port=5081

while [ $# -gt 0 ]; do
  case "$1" in
    --dir) dir=$2; shift 2 ;;
    --postgres) postgres=true; shift ;;
    --port) port=$2; shift 2 ;;
    --runner-port) runner_port=$2; shift 2 ;;
    -h|--help) sed -n '2,9p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option: $1 (see --help)" >&2; exit 2 ;;
  esac
done

say() { printf '%s\n' "$*"; }
fail() { printf 'install: %s\n' "$*" >&2; exit 1; }

command -v docker >/dev/null 2>&1 || fail "Docker is needed: https://docs.docker.com/engine/install/"
docker compose version >/dev/null 2>&1 || fail "Docker Compose v2 is needed ('docker compose')."
docker info >/dev/null 2>&1 || fail "Docker is not running, or this user may not use it."

here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/.." && pwd)
[ -f "$repo/src/AutoCanvas.Server/Dockerfile" ] ||
  fail "Run this script from a checkout of AutoCanvas: published images come with the first release."

say "Building the images (this takes a few minutes the first time)…"
docker build --quiet -f "$repo/src/AutoCanvas.Server/Dockerfile" -t autocanvas-server:local "$repo" >/dev/null
docker build --quiet -f "$repo/src/AutoCanvas.Runner/Dockerfile" -t autocanvas-runner:local "$repo" >/dev/null

mkdir -p "$dir"
dir=$(cd "$dir" && pwd)
cp "$here/compose/compose.yaml" "$here/compose/compose.postgres.yaml" "$here/compose/.env.example" "$dir/"
cd "$dir"

if [ ! -f .env ]; then
  project=$(basename "$dir" | tr 'A-Z' 'a-z' | tr -c 'a-z0-9_\n-' '-')
  files=compose.yaml
  password=
  if [ "$postgres" = true ]; then
    files=compose.yaml:compose.postgres.yaml
    password=$(od -An -N24 -tx1 /dev/urandom | tr -d ' \n')
  fi
  gid=$(docker run --rm -v /var/run/docker.sock:/s alpine:3.22 stat -c %g /s)
  {
    say "# Written by deploy/install.sh; see .env.example for every setting."
    say "COMPOSE_PROJECT_NAME=$project"
    say "COMPOSE_FILE=$files"
    say "AUTOCANVAS_PORT=$port"
    say "AUTOCANVAS_RUNNER_PORT=$runner_port"
    say "AUTOCANVAS_SERVER_IMAGE=autocanvas-server:local"
    say "AUTOCANVAS_RUNNER_IMAGE=autocanvas-runner:local"
    say "AUTOCANVAS_DOCKER_GID=$gid"
    say "AUTOCANVAS_POSTGRES_PASSWORD=$password"
  } > .env
  chmod 600 .env
  # The runner's volume must exist to know where it is on the Docker host.
  docker compose create >/dev/null
  volume=$(docker volume inspect -f '{{ .Mountpoint }}' "${project}_runner")
  say "AUTOCANVAS_RUNNER_HOST_DATA=$volume" >> .env
fi

say "Starting AutoCanvas…"
docker compose up -d --remove-orphans

# The server logs the setup address once it listens; an installation set up already logs none.
port=$(sed -n 's/^AUTOCANVAS_PORT=//p' .env)
token=
started=0
i=0
while [ $i -lt 120 ]; do
  logs=$(docker compose logs --no-color server 2>/dev/null || true)
  token=$(printf '%s\n' "$logs" | sed -n 's/.*setup?token=\([A-Za-z0-9_-]*\).*/\1/p' | tail -n 1)
  [ -n "$token" ] && break
  if printf '%s\n' "$logs" | grep -q 'Application started'; then
    started=$((started + 1))
    [ $started -ge 3 ] && break
  fi
  i=$((i + 1))
  sleep 2
done

say ""
say "AutoCanvas is running in $dir."
if [ -n "$token" ]; then
  say "Open http://<this machine's address>:$port/setup in a browser and enter the setup token:"
  say ""
  say "  $token"
  say ""
  say "The token changes when the server restarts; 'docker compose logs server' in $dir shows it again."
elif [ $started -ge 3 ]; then
  say "It is set up already; it was updated and restarted. Open http://<this machine's address>:$port"
else
  say "The server did not start in time. See 'docker compose logs server' in $dir."
  exit 1
fi
