# AGENTS.md — web UI

The browser UI of AutoCanvas: a React SPA built with Vite into the server's `wwwroot` and served by
`AutoCanvas.Server` ([ADR-0018](../../docs/adr/0018-web-ui.md)). The plan is
[S3](../../docs/plans/s3.md); the architecture page is `docs/technical/web-ui.md`. Commands are in
the root `AGENTS.md` and `package.json`.

## Where things go

| Path | Contents |
|---|---|
| `src/api/` | `client.ts` (openapi-fetch + CSRF/session middleware), `problem.ts`, generated `schema.d.ts` (never edit), one hooks file per area (`session.ts`, …) |
| `src/app/` | `App`, `routes.tsx`, session guards, theme, `localStorage` settings |
| `src/layout/` | App shell (sidebar, breadcrumbs, user menu), the sign-in layout |
| `src/pages/` | One file per page; a page's tests next to it |
| `src/ui/` | Small shared components; no API calls in here |
| `src/styles/theme.css` | All design tokens; components use token classes (`bg-surface`, `text-muted`, `border-line`), never raw colors |
| `src/test/` | `fakeServer.ts` (the API in component tests), `renderApp.tsx`, setup |
| `e2e/` | Playwright tests against the real server |

## Rules

- **Only the public API**: the UI talks to `/api/v1` and nothing else, with types generated from
  `openapi/v1.json` (`npm run api`, commit the result). When the UI needs something the API lacks,
  add it to the server (additively, with the OpenAPI snapshot) instead of working around it in the
  browser.
- **Every call goes through `api` and `unwrap`** from `src/api/client.ts`, so the CSRF header,
  problem details and the ended-session handling apply everywhere. Never call `fetch` directly.
- **The server decides**: permissions, validation and expressions are checked on the server. The
  UI hides or disables what the user's role does not allow, but never relies on that. Show field
  errors from the problem at their field (`ApiError.fieldErrors`), everything else with
  `ErrorAlert`.
- **YAML is the source of truth** in the editor. The canvas and the YAML editor change one `yaml`
  Document; comments, key order and unknown keys survive. Positions live in `x-editor.layout`.
  Never rebuild the file from a graph model.
- **No inline scripts**: the page's CSP allows scripts from the server's origin only. Inline
  `style` attributes are fine.
- **Gates** (exactly what CI runs): TypeScript strict (`exactOptionalPropertyTypes`,
  `noUncheckedIndexedAccess`) with no `any`, oxlint and Prettier without warnings, Vitest with at
  least 75 % line coverage, the Playwright smoke test. The same rule as in .NET applies: no blanket
  suppressions; a disabled rule is a single line with a comment saying why.
- **Packages** are pinned exactly, at least two weeks old, with a license on the shared list
  (`npm run licenses`, ADR-0019), and listed in `docs/technical/dependencies.md`.
- **Accessible by default**: real buttons and links, labels on every input (`TextField`), visible
  focus, keyboard paths for every action on the canvas, WCAG AA contrast in light and dark.
- **Texts are English**, short and in sentence case; buttons say what happens ("Change password",
  not "Submit"); errors say what went wrong and what to do. Dates and numbers use the browser's
  locale.
- **Tests**: logic in plain unit tests; components and flows with Testing Library by role and
  label, against `fakeServer`; whole flows in Playwright against the real server. Every page a step
  adds gets a screenshot in light and dark in the e2e tests, for the maintainer's review.
- **Harvesting** from the origin's `devops-web` follows the `harvest` skill: no homelab design
  system, OIDC, SignalR or service names.
