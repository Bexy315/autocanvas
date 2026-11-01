#!/usr/bin/env bash
# Runs AutoCanvas for development: the server (Development settings, SQLite in
# src/AutoCanvas.Server/data) and the web UI's Vite dev server, which sends /api to the server.
# Open http://localhost:5173. Ctrl+C stops both.
#
# The first start creates the admin below, who must choose a new password at the first sign-in.
# Later starts keep the existing accounts. Delete src/AutoCanvas.Server/data to start over.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
admin_user="admin"
admin_password="autocanvas-dev-admin"

cd "$root"
if [ ! -d src/web/node_modules ]; then
  (cd src/web && npm ci)
fi

dotnet run --project src/AutoCanvas.Server -- \
  "--Bootstrap:AdminUserName=$admin_user" "--Bootstrap:AdminPassword=$admin_password" &
server=$!
trap 'kill "$server" 2>/dev/null || true' EXIT INT TERM

# The first start builds the server, which takes a while; the UI starts once the server answers.
echo "Waiting for the server on http://localhost:5080 (the first start builds it) ..."
until curl -fsS -o /dev/null --max-time 2 http://localhost:5080/health/ready; do
  if ! kill -0 "$server" 2>/dev/null; then
    echo "The server stopped; see its output above." >&2
    exit 1
  fi
  sleep 1
done

echo
echo "  Web UI: http://localhost:5173 (API on http://localhost:5080)"
echo "  First start: sign in as '$admin_user' with '$admin_password', then choose a new password."
echo

cd src/web
npm run dev
