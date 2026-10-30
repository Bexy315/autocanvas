#!/usr/bin/env bash
# Adds an EF Core migration for every database provider at once (ADR-0010, skill add-migration).
# Usage: build/add-migration.sh <MigrationName>
set -euo pipefail

if [[ $# -ne 1 || ! "$1" =~ ^[A-Z][A-Za-z0-9]+$ ]]; then
  echo "usage: $0 <MigrationName>   (PascalCase, e.g. AddUsers)" >&2
  exit 2
fi

cd "$(dirname "$0")/.."
dotnet tool restore > /dev/null

for provider in Postgres Sqlite; do
  project="src/AutoCanvas.Persistence.$provider"
  echo "== $provider"
  dotnet ef migrations add "$1" --project "$project" --startup-project "$project" --output-dir Migrations
done
