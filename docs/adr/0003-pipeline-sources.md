# 0003. Pipelines live in repositories or are managed, one source each

- Status: Accepted
- Date: 2026-10-29

## Context

Two audiences expect different things. Developers expect pipelines as code: versioned with the
project, reviewed in pull requests, changing together with the code they build. Technicians
automating operational chores often have no repository and expect to build automations in a UI.
The origin engine stored pipelines in its database and edited them on a canvas or as YAML.

## Options

1. **Repository only** — like most hosted CI systems. Excludes the canvas-first use case.
2. **Database only** — like the origin. Excludes pipelines as code and pull-request review.
3. **Both, with a single source per pipeline** — every pipeline is either *repository* (the YAML
   file is the truth) or *managed* (the database is the truth).
4. **Both, synchronized** — the same pipeline edited in either place, kept in sync. Conflicts
   between a UI edit and a commit have no good resolution.

## Decision

Option 3. Both kinds use the same model and the same schema, so they run identically.

- Repository pipelines are discovered at `.autocanvas/*.yml` (or `.yaml`) in the repository root,
  one pipeline per file; the file name without extension is the default pipeline name.
- The canvas shows repository pipelines read-only. Editing one on the canvas will later produce a
  commit or pull request through the forge integration, never a silent divergence.
- Managed pipelines can be exported to YAML and moved into a repository, which turns them into
  repository pipelines.

## Consequences

- One parser, validator and schema serve both kinds.
- The server needs a repository-discovery step on push events and on demand (S5).
- A folder of pipeline files allows several pipelines per repository and keeps files small.
