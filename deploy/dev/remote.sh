#!/usr/bin/env bash
# The one command the dev instance's deploy key may run: ~deploy/.ssh/authorized_keys forces it
# (ADR-0023), and it takes what to do from SSH_ORIGINAL_COMMAND:
#
#   deploy <commit>   run the images of that commit; seeds the instance if it is fresh
#   reset             remove all data and seed a fresh instance
#   status            the containers and the running version
#
# On standard input come header lines up to an empty line, then optionally a tar.gz of the commit's
# deploy/compose and deploy/dev (tar -czf - -C deploy compose dev):
#
#   REGISTRY_TOKEN=…   for pulling the images (deploy); used once, never stored
#   SEED_PASSWORD=…    the seeded users' password (reset, and a deploy to a fresh instance)
#
# bootstrap.sh installs this file owned by root, so the deploy user cannot change it.
set -euo pipefail

home=/opt/autocanvas-dev
instance=$home/instance

log() { printf '%s %s\n' "$(date -u +%H:%M:%S)" "$*"; }
fail() {
  log "error: $*" >&2
  exit 1
}

# Run by hand (as root, for a test), the arguments stand in for the SSH command.
read -r -a words <<<"${SSH_ORIGINAL_COMMAND:-$*}"
action=${words[0]:-}
case "$action" in
  deploy)
    commit=${words[1]:-}
    [[ ${#words[@]} -eq 2 && $commit =~ ^[0-9a-f]{40}$ ]] || fail "usage: deploy <40-character commit>"
    ;;
  reset | status)
    [ ${#words[@]} -eq 1 ] || fail "usage: $action"
    ;;
  *) fail "allowed: deploy <commit>, reset, status" ;;
esac

cd "$instance"
# One at a time: a nightly reset waits for a deploy that is still running, and the other way round.
exec 9>"$instance/.lock"
flock --wait 900 9 || fail "another deploy or reset is still running."

registry_token=
seed_password=
if [ "$action" != status ]; then
  while IFS= read -r line; do
    [ -z "$line" ] && break
    case "$line" in
      REGISTRY_TOKEN=*) registry_token=${line#*=} ;;
      SEED_PASSWORD=*) seed_password=${line#*=} ;;
      *) fail "unknown header line '${line%%=*}'." ;;
    esac
  done
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

setting() { sed -n "s/^$1=//p" .env | tail -n 1; }
set_setting() {
  if grep -q "^$1=" .env; then sed -i "s|^$1=.*|$1=$2|" .env; else printf '%s=%s\n' "$1" "$2" >>.env; fi
}

# Takes the commit's compose files, Caddyfile and seed from standard input, if they came.
take_files() {
  cat >"$work/files.tar.gz"
  [ -s "$work/files.tar.gz" ] || return 0
  mkdir "$work/files"
  tar -xzf "$work/files.tar.gz" -C "$work/files" --no-same-owner
  local file
  for file in compose/compose.yaml compose/compose.postgres.yaml dev/compose.dev.yaml dev/Caddyfile dev/seed.sh; do
    [ -f "$work/files/$file" ] || fail "the files lack $file."
  done
  cp "$work/files/compose/compose.yaml" "$work/files/compose/compose.postgres.yaml" \
    "$work/files/dev/compose.dev.yaml" "$work/files/dev/Caddyfile" "$work/files/dev/seed.sh" "$instance/"
  chmod +x "$instance/seed.sh"
  log "Took the compose files and the seed of this commit."
}

wait_ready() {
  for _ in $(seq 180); do
    curl --silent --fail http://127.0.0.1:5080/health/ready >/dev/null 2>&1 && return 0
    sleep 1
  done
  docker compose logs --no-color --tail 50 server >&2 || true
  fail "the server was not ready within 180 s."
}

version() { curl --silent --fail http://127.0.0.1:5080/api/v1/info | sed -n 's/.*"version":"\([^"]*\)".*/\1/p'; }

# Seeds a fresh instance with the setup token from the server's log.
seed() {
  [ -n "$seed_password" ] || fail "the instance is fresh, but no SEED_PASSWORD came to seed it."
  local token=
  for _ in $(seq 120); do
    token=$(docker compose logs --no-color server 2>/dev/null | sed -n 's/.*setup?token=\([A-Za-z0-9_-]*\).*/\1/p' | tail -n 1)
    [ -n "$token" ] && break
    sleep 1
  done
  [ -n "$token" ] || fail "the server logged no setup token."
  AUTOCANVAS_SEED_PASSWORD=$seed_password "$instance/seed.sh" --url http://127.0.0.1:5080 --token "$token"
}

fresh() { [ "$(curl --silent --fail http://127.0.0.1:5080/api/v1/setup | jq -r .open)" = true ]; }

# Keeps the newest three commits' images of each kind, so a deploy can be undone by hand.
prune() {
  local repository name
  repository=$(setting AUTOCANVAS_IMAGE_REPOSITORY)
  for name in autocanvas-server autocanvas-runner; do
    docker image ls "$repository/$name" --format '{{.Repository}}:{{.Tag}}' | grep ':sha-' | tail -n +4 |
      while read -r image; do docker image rm "$image" >/dev/null 2>&1 || true; done
  done
}

case "$action" in
  status)
    docker compose ps --format 'table {{.Service}}\t{{.State}}\t{{.Status}}'
    echo "version: $(version || echo 'not answering')"
    ;;

  deploy)
    take_files
    [ -n "$registry_token" ] || fail "no REGISTRY_TOKEN came to pull the images."
    repository=$(setting AUTOCANVAS_IMAGE_REPOSITORY)
    set_setting AUTOCANVAS_SERVER_IMAGE "$repository/autocanvas-server:sha-$commit"
    set_setting AUTOCANVAS_RUNNER_IMAGE "$repository/autocanvas-runner:sha-$commit"
    # A registry login of its own for this run, removed with the work directory.
    export DOCKER_CONFIG=$work/docker
    printf '%s' "$registry_token" | docker login "${repository%%/*}" --username deploy --password-stdin >/dev/null
    log "Pulling the images of $commit…"
    docker compose pull --quiet
    docker logout "${repository%%/*}" >/dev/null
    unset DOCKER_CONFIG
    log "Starting…"
    docker compose up -d --remove-orphans --quiet-pull
    wait_ready
    running=$(version)
    [[ $running == *"+$commit" ]] || fail "the server runs $running, not $commit."
    log "Running $running."
    if fresh; then
      log "The instance is fresh; seeding it."
      seed
    fi
    prune
    log "Deployed $commit."
    ;;

  reset)
    take_files
    log "Removing the instance with all its data…"
    docker compose down --volumes --remove-orphans
    log "Starting a fresh one…"
    docker compose up -d --quiet-pull
    wait_ready
    seed
    log "Reset to the seed, running $(version)."
    ;;
esac
