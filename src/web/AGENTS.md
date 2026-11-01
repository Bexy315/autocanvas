# AGENTS.md — web UI

The browser UI of AutoCanvas: a React SPA built with Vite and served by `AutoCanvas.Server`
([ADR-0018](../../docs/adr/0018-web-ui.md)). The plan is [S3](../../docs/plans/s3.md). The app
itself arrives in S3 step 2; until then this file only holds the rules it starts from.

- **Only the public API**: the UI talks to `/api/v1` and nothing else, with types generated from
  `openapi/v1.json`. When the UI needs something the API lacks, add it to the server (additively,
  with the OpenAPI snapshot) instead of working around it in the browser.
- **The server decides**: permissions, validation and expressions are checked on the server. The
  UI hides or disables what the user's role does not allow, but never relies on that.
- **YAML is the source of truth** in the editor. The canvas and the YAML editor change one `yaml`
  Document; comments, key order and unknown keys survive. Positions live in `x-editor.layout`.
  Never rebuild the file from a graph model.
- **Gates** (exactly what CI runs): TypeScript strict with no `any` escape hatches, oxlint and
  Prettier without warnings, Vitest with at least 75 % line coverage, the Playwright smoke test.
  The same rule as in .NET applies: no blanket suppressions; a disabled rule is a single line with a
  comment saying why.
- **Packages** are pinned exactly, at least two weeks old, MIT-compatible, and listed in
  `docs/technical/dependencies.md`.
- **Accessible by default**: real buttons and links, labels on every input, visible focus, keyboard
  paths for every action on the canvas, WCAG AA contrast in light and dark.
- **Texts are English** and short; dates and numbers use the browser's locale.
- **Tests**: logic (YAML ↔ graph, layout, API wrapper) in plain unit tests; components with Testing
  Library by role and label, not by class or test id where a role exists; flows in Playwright
  against the real server.
- **Harvesting** from the origin's `devops-web` follows the `harvest` skill: no homelab design
  system, OIDC, SignalR or service names.
