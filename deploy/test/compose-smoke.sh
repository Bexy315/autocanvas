#!/usr/bin/env bash
# The compose package end to end, as CI runs it: install with deploy/install.sh, set up through the
# API with the token the script printed, wait for the bundled runner, run the example automation
# and check its tests and coverage. With --seed, the dev instance's seed (deploy/dev/seed.sh) sets
# it up instead, and the test checks what the seed made.
#
#   deploy/test/compose-smoke.sh <instance directory> [--postgres] [--seed]
set -euo pipefail

dir=$1
shift
seed=false
install=()
for argument in "$@"; do
  if [ "$argument" = --seed ]; then seed=true; else install+=("$argument"); fi
done
base=http://localhost:5080
here=$(cd "$(dirname "$0")" && pwd)
cookies=$(mktemp)

dump() {
  echo "::group::docker compose logs"
  (cd "$dir" && docker compose logs --no-color) || true
  echo "::endgroup::"
}
trap 'status=$?; if [ $status -ne 0 ]; then dump; fi; rm -f "$cookies"' EXIT

api() {
  curl --silent --show-error --fail-with-body -b "$cookies" -c "$cookies" -H 'X-AutoCanvas-Request: 1' "$@"
}

# Waits until a command prints something, at most $1 seconds.
eventually() {
  local seconds=$1 out
  shift
  for _ in $(seq "$seconds"); do
    if out=$("$@") && [ -n "$out" ]; then
      printf '%s' "$out"
      return 0
    fi
    sleep 1
  done
  echo "Gave up after ${seconds}s: $*" >&2
  return 1
}

output=$("$here/../install.sh" --dir "$dir" "${install[@]}")
printf '%s\n' "$output"
token=$(printf '%s\n' "$output" | sed -n 's/^  \([A-Za-z0-9_-]\{20,\}\)$/\1/p' | head -n 1)
[ -n "$token" ] || { echo "The install script printed no setup token." >&2; exit 1; }

if [ "$seed" = true ]; then
  password='seeded password for the compose check'
  AUTOCANVAS_SEED_PASSWORD=$password "$here/../dev/seed.sh" --url "$base" --token "$token"

  echo "Checking the seeded users…"
  # Signs in as a seeded user and prints the names of Team's subfolders they see.
  sees() {
    local own team
    own=$(mktemp)
    curl --silent --show-error --fail-with-body -b "$own" -c "$own" -H 'X-AutoCanvas-Request: 1' -H 'Content-Type: application/json'       -d "$(jq -n --arg u "$1" --arg p "$password" '{userName: $u, password: $p}')" "$base/api/v1/auth/sign-in" >/dev/null
    team=$(curl --silent --fail -b "$own" "$base/api/v1/folders" | jq -r '.[] | select(.name == "Team") | .id')
    curl --silent --fail -b "$own" "$base/api/v1/folders/$team/children" | jq -r '[.items[].name] | sort | join(",")'
    rm -f "$own"
  }
  [ "$(sees viewer)" = Builds ] || { echo "viewer should see Builds only." >&2; exit 1; }
  [ "$(sees operator)" = Builds ] || { echo "operator should see Builds only." >&2; exit 1; }
  [ "$(sees editor)" = Builds,Restricted ] || { echo "editor should see Builds and Restricted." >&2; exit 1; }
  [ -n "$(sees admin)" ] || { echo "admin cannot sign in with the seed password." >&2; exit 1; }

  echo "Seeding again changes nothing…"
  again=$(AUTOCANVAS_SEED_PASSWORD=$password "$here/../dev/seed.sh" --url "$base" --token "$token")
  grep -q 'nothing changed' <<<"$again" || { echo "A second seed did something: $again" >&2; exit 1; }

  echo "The compose package works with the dev instance's seed."
  exit 0
fi

echo "Setting up…"
completed=$(api -H "X-AutoCanvas-Setup-Token: $token" -H 'Content-Type: application/json' \
  -d '{"admin":{"userName":"root","password":"a long enough password"},"publicAddress":null,"runnerAddress":null,"bundledRunner":true,"example":true}' \
  "$base/api/v1/setup")
example=$(jq -r '.exampleAutomationId' <<<"$completed")
jq -e '.masterKey.key | length == 44' <<<"$completed" >/dev/null

echo "Waiting for the bundled runner…"
online() { api "$base/api/v1/runners" | jq -r '.items[] | select(.name == "bundled" and .isOnline) | .id'; }
eventually 120 online >/dev/null

echo "Running the example…"
run=$(api -X POST "$base/api/v1/automations/$example/runs" | jq -r '.id')
ended() { api "$base/api/v1/runs/$run" | jq -r 'select(.state != "Running") | .state'; }
state=$(eventually 300 ended)
[ "$state" = Succeeded ] || { echo "The example run ended $state." >&2; api "$base/api/v1/runs/$run" >&2; exit 1; }

reports=$(api "$base/api/v1/runs/$run/reports")
jq -e '.tests.total == 3 and .tests.skipped == 1 and .tests.failed == 0' <<<"$reports" >/dev/null ||
  { echo "Unexpected tests: $reports" >&2; exit 1; }
jq -e '.coverage.linesCovered == 4 and .coverage.linesValid == 5' <<<"$reports" >/dev/null ||
  { echo "Unexpected coverage: $reports" >&2; exit 1; }

echo "The compose package works: set up, the bundled runner ran the example with its reports."
