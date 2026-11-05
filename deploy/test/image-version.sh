#!/usr/bin/env bash
# Checks that a server image names the commit it was built from: starts it briefly (GET /api/v1/info
# answers in setup mode too) and compares the version's source revision.
#
#   deploy/test/image-version.sh <server image> <commit>
set -euo pipefail

image=$1
revision=$2
name="autocanvas-image-version-$$"

trap 'docker rm --force "$name" >/dev/null 2>&1 || true' EXIT
docker run --detach --name "$name" --publish 127.0.0.1::5080 "$image" >/dev/null
port=$(docker port "$name" 5080/tcp | head -n 1 | sed 's/.*://')

version=
for _ in $(seq 60); do
  # Read with sed, so the check needs nothing beyond curl: {"product":"AutoCanvas","version":"…"}.
  if version=$(curl --silent --fail "http://127.0.0.1:$port/api/v1/info" | sed -n 's/.*"version":"\([^"]*\)".*/\1/p') &&
    [ -n "$version" ]; then
    break
  fi
  sleep 1
done

if [ -z "$version" ]; then
  echo "The server in $image did not answer within 60 s." >&2
  docker logs "$name" >&2 || true
  exit 1
fi

case "$version" in
  *"+$revision")
    echo "$image is version $version."
    ;;
  *)
    echo "$image is version '$version', not built from $revision." >&2
    exit 1
    ;;
esac
