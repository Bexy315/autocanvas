# 0007. Automation format version 1

- Status: Accepted
- Date: 2026-10-30

## Context

The automation format is AutoCanvas's most visible public contract: users write it by hand, the
canvas reads and writes it, and every later version must stay compatible with files already in
repositories. The origin engine had a working format (`trigger`, jobs with `depends_on`, steps with
`type` + `config`, switch and join jobs, outputs, retry, finally jobs, services, `changed_when`,
`concurrency: single`). AutoCanvas reviewed it deliberately instead of copying it
([ADR-0001](0001-clone-and-diverge.md)), with these goals: familiar to people who know other CI
systems, short for the common case, unambiguous for the canvas, and machine-checkable.

## Decision

The format is defined by the JSON Schema `schema/automation.v1.json` and documented in the user
guide. Compared with the origin:

- **Kept** because they work: jobs as a map, `switch`/`join` flow-control jobs, explicit job
  `outputs`, `retry`, `finally`/`on_cancel`, `changed_when`, `concurrency`, `services` with
  presets, `${{ }}` expressions, snake_case keys, `x-editor` for the canvas layout (generalized to
  any `x-…` key).
- **Required `version: 1`** so later versions can be told apart without guessing.
- **`triggers:`** as a map (`manual`, `push` with branches/tags/paths, `schedule` as a list of
  cron or interval entries, `webhook`, `automation_completed`). Not `on:`, which YAML 1.1 parsers
  read as a boolean and which suggests GitHub Actions semantics AutoCanvas does not have.
- **Steps are `type` + `with`**, plus the shorthand `run:` for scripts — the most common step needs
  one line.
- **Step `id`** separate from the display `name`; outputs are read through ids, so names may
  contain spaces.
- **`needs`** accepts plain job ids or objects with `on` or `case`; it replaces `depends_on`.
- **`if:`** on jobs and steps, with a small condition language (literals, `==`, `!=`, `&&`, `||`,
  `!`, parentheses). Functions are left for later minor additions.
- **`image`** on the job replaces the origin's named build environments, which depended on its
  package registry.
- **Repository automations check out their own commit by default**; `checkout: false` opts out,
  a list checks out other repositories. Managed automations check out nothing implicitly.
- **`reports`** on the job publish JUnit, TRX and Cobertura files as first-class results.
- **Script outputs** are written as `name=value` lines to the file in `$AUTOCANVAS_OUTPUT`.
- **Dropped**: platform-event triggers, the workflow-level legacy checkout, the `when` alias of
  `case`, registry-specific step fields.

## Consequences

- Origin automations do not load unchanged; converting them is mechanical (renames and moves),
  and a converter can be written if needed.
- Within version 1 only additive changes are allowed: new optional keys, new trigger kinds, new
  node types, new expression functions. Anything else is version 2, and AutoCanvas keeps reading
  version 1.
- The schema gives editor completion and validation for free; semantic rules it cannot express
  (needs pointing to existing jobs, no cycles, expressions reading only what is reachable, at most
  one default case) are checked by the validator in `AutoCanvas.Core`.
