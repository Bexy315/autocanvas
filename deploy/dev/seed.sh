#!/usr/bin/env bash
# Fills a fresh AutoCanvas instance with what manual tests need (D1, deploy/dev/README.md): test
# users with different roles, a group, folders with automations, a variable and a secret, and a
# first run of each automation. Everything goes through the public API, as a person in the web UI
# would do it. On an instance that is set up already it changes nothing.
#
#   AUTOCANVAS_SEED_PASSWORD=… deploy/dev/seed.sh --url <address> --token <setup token> \
#     [--public-address <https://…>]
#
# All users get the same password (at least 12 characters). Needs bash, curl and jq.
set -euo pipefail

url=
token=
public=
while [ $# -gt 0 ]; do
  case "$1" in
    --url) url=${2%/}; shift 2 ;;
    --token) token=$2; shift 2 ;;
    --public-address) public=${2%/}; shift 2 ;;
    *) echo "seed: unknown argument '$1'" >&2; exit 2 ;;
  esac
done
password=${AUTOCANVAS_SEED_PASSWORD:-}

fail() { echo "seed: $*" >&2; exit 1; }
[ -n "$url" ] || fail "--url is needed, such as http://localhost:5080."
[ ${#password} -ge 12 ] || fail "AUTOCANVAS_SEED_PASSWORD must have at least 12 characters."
command -v jq >/dev/null 2>&1 || fail "jq is needed."

cookies=$(mktemp)
trap 'rm -f "$cookies"' EXIT

api() {
  curl --silent --show-error --fail-with-body -b "$cookies" -c "$cookies" -H 'X-AutoCanvas-Request: 1' "$@"
}
# A JSON body for POST/PUT, built with jq so that no value needs quoting by hand.
send() {
  local method=$1 path=$2
  shift 2
  api -X "$method" -H 'Content-Type: application/json' -d "$(jq -n "$@")" "$url$path"
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
  echo "seed: gave up after ${seconds}s: $*" >&2
  return 1
}

if [ "$(api "$url/api/v1/setup" | jq -r '.open')" != true ]; then
  echo "seed: $url is set up already; nothing changed."
  exit 0
fi
[ -n "$token" ] || fail "--token is needed: the setup token from the server's log."

echo "Setting up $url…"
api -X POST -H "X-AutoCanvas-Setup-Token: $token" -H 'Content-Type: application/json' \
  -d "$(jq -n --arg p "$password" --arg a "$public" \
    '{admin: {userName: "admin", password: $p}, publicAddress: (if $a == "" then null else $a end),
      runnerAddress: null, bundledRunner: true, example: true}')" \
  "$url/api/v1/setup" >/dev/null

echo "Users and a group…"
# Users an admin creates change their password when they first sign in. The seed does that for them,
# as they would, so that they can sign in with the seed password right away.
initial="$password (initial)"
user() {
  local id own
  id=$(send POST /api/v1/users --arg u "$1" --arg d "$2" --arg p "$initial" '{userName: $u, displayName: $d, password: $p}' | jq -r .id)
  own=$(mktemp)
  curl --silent --show-error --fail-with-body -b "$own" -c "$own" -H 'X-AutoCanvas-Request: 1' \
    -H 'Content-Type: application/json' -d "$(jq -n --arg u "$1" --arg p "$initial" '{userName: $u, password: $p}')" \
    "$url/api/v1/auth/sign-in" >/dev/null
  curl --silent --show-error --fail-with-body -b "$own" -c "$own" -H 'X-AutoCanvas-Request: 1' -X PUT \
    -H 'Content-Type: application/json' -d "$(jq -n --arg c "$initial" --arg p "$password" '{currentPassword: $c, newPassword: $p}')" \
    "$url/api/v1/me/password" >/dev/null
  rm -f "$own"
  printf '%s' "$id"
}
viewer=$(user viewer 'Vera Viewer')
operator=$(user operator 'Otto Operator')
editor=$(user editor 'Edith Editor')
testers=$(send POST /api/v1/groups '{name: "testers", description: "The seeded test users."}' | jq -r .id)
for member in "$viewer" "$operator" "$editor"; do
  api -X PUT "$url/api/v1/groups/$testers/members/$member" >/dev/null
done

echo "Folders, grants, a variable and a secret…"
folder() { send POST /api/v1/folders --arg n "$1" --arg p "${2:-}" --arg d "$3" \
  '{name: $n, parentId: (if $p == "" then null else $p end), description: $d}' | jq -r .id; }
grant() { send PUT "/api/v1/folders/$1/grants" --arg r "$2" --arg k "$3" --arg i "$4" \
  '{role: $r} + (if $k == "user" then {userId: $i} else {groupId: $i} end)' >/dev/null; }
team=$(folder Team '' 'Seeded: the testers group may view it, operator may run, editor may edit.')
builds=$(folder Builds "$team" 'Seeded automations with runs, tests and coverage.')
restricted=$(folder Restricted "$team" 'Seeded: restricted, so only editor sees it.')
grant "$team" Viewer group "$testers"
grant "$team" Operator user "$operator"
grant "$team" Editor user "$editor"
send PUT "/api/v1/folders/$restricted/restriction" '{isRestricted: true}' >/dev/null
grant "$restricted" Editor user "$editor"
send PUT "/api/v1/folders/$team/variables/GREETING" '{value: "Hello from the seeded Team folder"}' >/dev/null
secret="dev-$(head -c 12 /dev/urandom | od -An -tx1 | tr -d ' \n')"
send PUT "/api/v1/folders/$team/secrets/DEPLOY_TOKEN" --arg v "$secret" '{value: $v}' >/dev/null

# Checks an automation file first, so that a broken one says why.
automation() {
  local folder=$1 name=$2 yaml=$3 check
  check=$(send POST /api/v1/automations/validate --arg y "$yaml" --arg f "$folder" '{yaml: $y, folderId: $f}')
  if [ "$(jq -r .valid <<<"$check")" != true ]; then
    jq -r '.diagnostics[] | "\(.code) \(.path): \(.message)"' <<<"$check" >&2
    fail "the seeded automation '$name' is not valid."
  fi
  send POST "/api/v1/folders/$folder/automations" --arg n "$name" --arg y "$yaml" \
    '{name: $n, yaml: $y, message: "Seeded"}' | jq -r .id
}

echo "Automations…"
build_and_test=$(automation "$builds" build-and-test "$(cat <<'YAML'
# Seeded by the dev instance: two jobs, the second with a test and a coverage report.
version: 1
name: Build and test
description: Builds, then tests and publishes the reports. Also runs every six hours.

triggers:
  manual: true
  schedule:
    - interval: 6h

jobs:
  build:
    runs_on: linux
    image: alpine:3.22
    steps:
      - name: Build
        run: echo "Building at $(date -u +%Y-%m-%dT%H:%M:%SZ)"

  test:
    needs: [build]
    runs_on: linux
    image: alpine:3.22
    steps:
      - name: Test
        run: |-
          cat > TEST-shop.xml <<'EOF'
          <testsuite name="shop" tests="5" skipped="1">
            <testcase classname="Shop.Cart" name="adds_an_item" time="0.012"/>
            <testcase classname="Shop.Cart" name="removes_an_item" time="0.008"/>
            <testcase classname="Shop.Cart" name="totals_the_prices" time="0.004"/>
            <testcase classname="Shop.Checkout" name="takes_a_card" time="0.031"/>
            <testcase classname="Shop.Checkout" name="takes_a_voucher" time="0"><skipped message="Not built yet"/></testcase>
          </testsuite>
          EOF
          cat > coverage.xml <<'EOF'
          <coverage line-rate="0.75" branch-rate="0" lines-covered="6" lines-valid="8" version="1" timestamp="0">
            <packages><package name="Shop"><classes>
              <class name="Shop.Cart" filename="Cart.cs" line-rate="1"><lines>
                <line number="1" hits="4"/><line number="2" hits="4"/><line number="3" hits="2"/><line number="4" hits="1"/>
              </lines></class>
              <class name="Shop.Checkout" filename="Checkout.cs" line-rate="0.5"><lines>
                <line number="1" hits="1"/><line number="2" hits="1"/><line number="3" hits="0"/><line number="4" hits="0"/>
              </lines></class>
            </classes></package></packages>
          </coverage>
          EOF
          echo "Wrote the test and coverage reports"
    reports:
      - format: junit
        path: TEST-shop.xml
      - format: cobertura
        path: coverage.xml
YAML
)")
uses_secret=$(automation "$builds" uses-secret "$(cat <<'YAML'
# Seeded by the dev instance: reads the Team folder's variable and secret; the log masks the secret.
version: 1
name: Uses a secret
description: Prints the folder's greeting and uses its secret, which the log shows masked.

triggers:
  manual: true

jobs:
  greet:
    runs_on: linux
    image: alpine:3.22
    env:
      TOKEN: ${{ secrets.DEPLOY_TOKEN }}
    steps:
      - name: Greet
        run: echo "${{ vars.GREETING }}"
      - name: Use the token
        run: test -n "$TOKEN" && echo "The token is $TOKEN"
YAML
)")
fails=$(automation "$builds" fails-on-purpose "$(cat <<'YAML'
# Seeded by the dev instance: always fails, so there is a failed run to look at.
version: 1
name: Fails on purpose
description: Its only step exits with 1.

triggers:
  manual: true

jobs:
  check:
    runs_on: linux
    image: alpine:3.22
    steps:
      - name: Check something
        run: echo "Something is wrong" >&2 && exit 1
YAML
)")
automation "$restricted" deploy "$(cat <<'YAML'
# Seeded by the dev instance, in a restricted folder only editor (and admins) can see.
version: 1
name: Deploy
description: Only for those who may see the restricted folder.

triggers:
  manual: true

jobs:
  deploy:
    runs_on: linux
    image: alpine:3.22
    steps:
      - run: echo "Deploying (not really)"
YAML
)" >/dev/null
hello=$(api "$url/api/v1/folders" | jq -r '.[] | select(.name == "Getting started") | .id' |
  while read -r id; do api "$url/api/v1/folders/$id/automations" | jq -r '.items[] | select(.name == "hello") | .id'; done)
[ -n "$hello" ] || fail "the setup's example automation is missing."

echo "Waiting for the bundled runner…"
online() { api "$url/api/v1/runners" | jq -r '.items[] | select(.isOnline) | .id'; }
eventually 180 online >/dev/null

echo "First runs…"
declare -A expected=([hello]=Succeeded [build-and-test]=Succeeded [uses-secret]=Succeeded [fails-on-purpose]=Failed)
declare -A ids=([hello]=$hello [build-and-test]=$build_and_test [uses-secret]=$uses_secret [fails-on-purpose]=$fails)
declare -A runs
for name in "${!ids[@]}"; do
  runs[$name]=$(api -X POST "$url/api/v1/automations/${ids[$name]}/runs" | jq -r .id)
done
for name in "${!runs[@]}"; do
  ended() { api "$url/api/v1/runs/${runs[$name]}" | jq -r 'select(.state != "Running") | .state'; }
  state=$(eventually 300 ended)
  [ "$state" = "${expected[$name]}" ] || fail "the first run of $name ended $state, not ${expected[$name]}."
  echo "  $name: $state"
done
reports=$(api "$url/api/v1/runs/${runs[build-and-test]}/reports")
jq -e '.tests.total == 5 and .tests.skipped == 1 and .tests.failed == 0 and .coverage.linesValid == 8' \
  <<<"$reports" >/dev/null || fail "build-and-test's reports are not as seeded: $reports"

cat <<EOF

Seeded ${public:-$url}:
  users      admin (admin), viewer, operator, editor — all with the seed password
  group      testers (viewer, operator, editor): Viewer on Team
  folders    Getting started; Team (operator: Operator, editor: Editor);
             Team / Builds; Team / Restricted (restricted, editor only)
  runs       hello, build-and-test, uses-secret succeeded; fails-on-purpose failed
EOF
