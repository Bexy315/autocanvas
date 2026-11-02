---
sidebar_position: 5.5
---

# Web UI

The web UI is a React single-page app in `src/web`, built with Vite and served by the server itself
from its web root, on the same origin as the API ([ADR-0018](../adr/0018-web-ui.md)). It uses only
the public HTTP API under `/api/v1`, like any other client.

## How it is served

`npm run build` writes the app to `src/AutoCanvas.Server/wwwroot` (not committed); the server image
builds it in a stage of its own. The server then answers:

| Request | Answer |
|---|---|
| `/assets/…` | The app's files. Their names contain a hash of their content, so they are cached for a year (`immutable`). |
| Any other `GET` the server has no route for, e.g. `/account` | `index.html`, so the app's own routes work on reload. Checked on every load (`no-cache`), so a new version shows at once. |
| An unknown path under `/api`, `/health` or `/openapi` | A `404` problem, never the app. |

Without a built UI the server works as before and answers those paths with `404`; `dotnet build` and
`dotnet test` do not need Node.

Every response carries `X-Content-Type-Options: nosniff` and `Referrer-Policy: same-origin`.
`index.html` carries a content security policy that allows scripts, styles, images and connections
from the server's own origin only and forbids framing:

```
default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:;
connect-src 'self'; font-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self';
frame-ancestors 'none'
```

Inline styles are allowed because React and the canvas position elements with `style` attributes;
inline scripts are not, which is why the color scheme is set by a small file of its own
(`public/theme.js`) before the app loads.

## Talking to the API

- **Types** come from `openapi/v1.json`: `npm run api` generates `src/api/schema.d.ts`, which is
  committed; CI regenerates it and fails when it differs.
- **Client**: `openapi-fetch` with one middleware (`src/api/client.ts`). It sets
  `X-AutoCanvas-Request: 1` on every request that changes something, as the server's CSRF check
  requires, and reports a `401` on any request but the sign-in and the account check as an ended
  session. The app then forgets the user and asks to sign in again, returning to the same page.
- **Errors** are problem details (`src/api/problem.ts`): a broken rule (`field` with `detail`)
  appears at its field, problems in an automation file (`diagnostics`) as a list with line, column
  and code, everything else as a message with the server's words.
- **Data** is cached with TanStack Query, with one hooks file per area (`session.ts`, `folders.ts`,
  `automations.ts`); a change invalidates what it affects. A request the server refused (4xx) is
  not retried; an unreachable server (no connection, 502–504) is retried for about half a minute,
  other server errors twice.
- **Concurrency**: folders and automations are read with their `ETag` (`unwrapWithTag`), and
  changes send it back as `If-Match`. When someone changed the resource in between, the server
  answers `412`; the UI says so and shows the current state, so nothing is overwritten unseen.

## Structure

```
src/web/
  index.html, public/       page skeleton, favicon, theme.js (color scheme before the app loads)
  src/api/                  client, problems, generated schema, hooks per area
  src/app/                  App, routes, session guards, theme, settings in localStorage
  src/layout/               app shell (sidebar, breadcrumbs, user menu), folder tree, page context
  src/pages/                one file per page; pages/automation/ for an automation's tabs
  src/dialogs/              create, rename, move and delete dialogs
  src/lib/                  logic without React: line diff, roles, relative times
  src/ui/                   small shared components (button, field, dialog, badge, alert, …)
  src/styles/theme.css      design tokens: colors for light and dark, radius, fonts
  e2e/                      Playwright tests against the real server
```

## The YAML editor

The editor tab (`pages/automation/EditorTab.tsx`) wraps CodeMirror 6 (`editor/YamlEditor.tsx`) and
is loaded on demand, so the app's first load does not carry it. It keeps the version the edits are
based on; Save sends its ETag as `If-Match`, and a `412` opens a comparison of the newer version with
the edited text (`dialogs/ConflictDialog.tsx`).

- **Checks** come from the server: `useValidation` sends the text to
  `POST /api/v1/automations/validate` once typing pauses for 400 ms, cancels a check that is still
  running when a newer one starts, and keeps the last result visible meanwhile. The browser holds no
  copy of the rules (ADR-0018).
- **Suggestions** come from `schema/automation.v1.json`, imported at build time, and the node type
  catalog (`GET /api/v1/node-types`). `editor/yamlPath.ts` finds the path at the cursor from the
  indentation, which works on half-typed lines; `editor/schemaKeys.ts` walks the schema along it.
- **Colors** are the design tokens as CSS variables (`editor/editorTheme.ts`), so the editor follows
  light and dark without being rebuilt.
- **Keyboard**: Tab indents; Esc, then Tab, leaves the editor (WCAG 2.1.2).

The build splits libraries into a `vendor` chunk and the editor's libraries into the editor's own
chunk (`vite.config.ts`), so releases that only change the app leave the libraries cached.

Pages tell the shell where they are with `usePageInfo`: their breadcrumbs, and the folder to
highlight and expand in the folder tree. The tree follows the ARIA tree pattern (one tab stop,
arrow keys, Home and End) and loads subfolders when one is expanded; the move dialog uses the same
tree as a picker.

All colors come from the tokens in `theme.css`; light and dark share token names, and
`<html data-theme>` picks the values. Text and accent pairs meet WCAG AA.

## Developing

```sh
build/dev.sh            # or build/dev.ps1 on Windows: the server and Vite together
```

Open `http://localhost:5173`. Vite sends `/api` to the server on `http://localhost:5080`
(`AUTOCANVAS_SERVER_URL` changes that), so the browser sees one origin as in production. The server keeps its
SQLite database in `src/AutoCanvas.Server/data`; delete it to start over. The first
start creates the admin `admin` with the password `autocanvas-dev-admin`, which must be changed at
the first sign-in.

The gates, from `src/web` (exactly what CI runs):

```sh
npm ci
npm run api && git diff --exit-code -- src/api/schema.d.ts
npm run typecheck        # TypeScript strict
npm run lint             # oxlint, warnings fail
npm run format:check     # Prettier
npm test                 # Vitest, ≥ 75 % line coverage
npm run licenses         # npm licenses (ADR-0019)
npm run build
npm run e2e              # Playwright against the built server and UI
```

`npm run e2e` starts the server with `dotnet run` on ports 5090 and 5091 with a fresh SQLite
database, and saves screenshots of each screen in light and dark to `test-results/screenshots`
(CI keeps them as the artifact `web-screenshots`). It needs a browser once:
`npx playwright install chromium --only-shell`.
