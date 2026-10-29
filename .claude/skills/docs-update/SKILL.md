---
name: docs-update
description: Decide which AutoCanvas documentation a change affects (user docs, technical docs, code docs, generated references, plans) and update it in the same PR. Use for every change that alters behavior, configuration, a public API, architecture or the dev workflow.
---

# Updating documentation

Documentation is part of done. It ships in the same PR as the change, never as a follow-up.

## Where things go

| Change | Update |
|---|---|
| Something a user does or sees changes: pipeline syntax, UI, CLI, setup, configuration, error messages | `docs/user/` |
| How the system is built or run changes: components, data flow, security model, deployment, performance | `docs/technical/` |
| A decision between alternatives was made | `docs/adr/` (skill: `write-adr`) |
| A public type or member is added or its behavior changes | XML doc comment on it |
| A dependency is added, removed or replaced | `docs/technical/dependencies.md` |
| A build/test/CI command or a repository rule changes | `AGENTS.md` (and the nested one for the area), `README.md` if it is in the Development section |
| A plan step is finished or deviates from the plan | the active file in `docs/plans/` |

A change often touches several rows. Go through the whole table each time.

## Generated references

Some reference pages are generated from source files; edit the source, not the output, then
regenerate:

- automation format: `schema/automation.v*.json` is published on the docs site as-is (copied by
  `docs-site/scripts/copy-schemas.mjs`); when the schema changes, update `docs/user/automations.md`
  and the examples in `schema/examples/` in the same PR
- runner protocol reference ← `proto/runner/v*/` (from S0 step 5)
- node reference ← node manifests (later)

CI fails when committed generated output is stale. The generator commands will be listed here as
they are introduced.

## The docs site

`docs-site/` renders `docs/user`, `docs/technical` and `docs/adr` with Docusaurus; each folder is
one sidebar, ordered by `sidebar_position` front matter, then file name. `docs/plans/` is excluded.
Files are CommonMark (`.md`), not MDX — write them for GitHub and they render the same on the site.
Admonitions (`:::note`, `:::caution`) are the one site-specific syntax worth using.

Build it locally before pushing (`cd docs-site && npm run build`) — broken links fail CI.

## Writing well

- **User docs** are task-oriented: start with what the reader wants to achieve, show a working
  example, then explain options. Every example must be valid against the current schema.
- **Technical docs** explain structure and reasoning; link ADRs instead of repeating them.
- **XML docs** state intent, contracts (null handling, exceptions, units) and non-obvious behavior.
  `/// <summary>Gets the name.</summary>` on `Name` adds nothing — say what the name is used for.
- Use relative links between docs so they work both on GitHub and on the docs site.
- Keep one topic per page; prefer updating an existing page over adding a near-duplicate.
