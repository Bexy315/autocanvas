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

The editor (`pages/automation/EditorTab.tsx`) wraps CodeMirror 6 (`editor/YamlEditor.tsx`) and
is loaded on demand, so the app's first load does not carry it. It has a route of its own outside the
app shell (`/automations/:id/edit`, `pages/automation/EditorLayout.tsx`) and fills the window: a bar
with the way back, the view switch, the status and Save; canvas and file below; problems in a bar at
the bottom that folds away (remembered in `localStorage`). It keeps the version the edits are based
on; Save sends its ETag as `If-Match`, and a `412` opens a comparison of the newer version with the
edited text (`dialogs/ConflictDialog.tsx`).

- **Saving** is two steps: Save, and Ctrl+S anywhere on the page (one listener on the window; the
  file's own key binding marks the event as handled), open `pages/automation/SaveDialog.tsx`, which
  asks for the message and says how much changed (`changeSummary` in `lib/diff.ts`); Enter saves,
  Esc does not. A reflexive Ctrl+S after every small change would otherwise leave a trail of
  versions.

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

The build splits the libraries into groups (`vite.config.ts`): `vendor` for the app as a whole,
and `yaml-editor` (CodeMirror) and `canvas` (React Flow, dagre, the YAML parser), which only the
editor tab imports, so they load when the editor is first opened. Releases that only change the app
leave the libraries cached.

## The canvas

The canvas (`canvas/`) shows the file being edited as a graph, next to the YAML or alone, and lets
editors change it. Its logic is harvested from the origin's editor and rewritten for format v1.

- **Model** (`canvas/model.ts`): `graphFromYaml` reads the text with the `yaml` package into the
  trigger summary, one card per job (steps, switch cases, hints such as services, retry, limits),
  and the lines between them: from `needs` (success, failure, always or a switch case), from the
  trigger to every job without needs, and dashed into `finally` jobs from the jobs nothing waits
  for. Those last two are drawn, never stored. A need of a job that does not exist is marked on
  the card instead of dropped.
- **Layout** (`canvas/layout.ts`): positions saved in `x-editor.layout` stay; cards without one are
  placed by dagre, top to bottom; finally jobs are kept below the rest.
- **Following the text**: the canvas reads the text 150 ms after typing stops, but a text the
  canvas made itself at once (otherwise a dropped card would jump back for the pause). While the text cannot
  be read, the last readable graph stays, dimmed, with a note. `canvas/lines.ts` knows which lines
  each job takes, from the YAML document's node ranges: choosing a card puts the cursor on its job
  without taking the focus, and the job the cursor is in is selected on the canvas. The canvas
  reacts only to selections the user makes, so the two never echo each other.
- **Accessibility**: every card has a label that says what it is, what it needs and how many
  problems it has; cards are in reading order for Tab, and Enter or Space shows the job in the
  file. The **List** view shows the same structure as text. Colors of lines are repeated as labels
  (`on failure`, `always`, case names).
- **Editing** (editors, while the file can be read): the canvas reports intents (move, add,
  connect, disconnect, remove, undo, redo); `canvas/operations.ts` turns each into a new text, and
  the editor tab hands that to CodeMirror as the smallest change (`editor/smallestChange.ts`), so
  there is one undo history for typing and canvas. Operations always start from the editor's
  current text, as React Flow can report two in one event (a card and its lines deleted). Before
  the first change, every card without a saved place gets the place it is shown at
  (`pinUnplaced`), so nothing jumps as the graph changes. The size React Flow measured for each
  card is handed back with the cards: they are built anew on every change, and React Flow hides a
  card without a size (and its lines) until it has measured it again, which flickered while
  dragging.
- **Changing the text** (`canvas/yamlEdit.ts`): `setIn`, `deleteIn`, `appendItem`, `insertItem`,
  `removeItem`, `moveItem` and `renameKey` splice the text at the ranges of the parsed YAML document
  and render new values in the file's style (flow or block like their neighbours, scripts of several
  lines as `|` blocks), instead of writing the whole document, which would normalise unrelated
  lines. A new key goes where it usually is (`keyOrder` in `canvas/operations.ts`: `image` before
  `steps`, …), so a value cleared and typed again lands where it was. Each result
  is parsed again and compared (ignoring key order) with the same change on the document; when it
  differs, the whole document is written as a last resort and the change is marked `rewritten`.
- **Side panel** (`canvas/panel/`): opens for the card chosen on the canvas (`onPick`) and follows
  the choice. It reads values from the same text as the canvas (`canvas/panelModel.ts`), showing
  each in a form only when it has the shape that form writes back, and otherwise as written, to be
  changed in the YAML. Each field change is a `Change` from `canvas/operations.ts` (`setField`,
  `addStep`, `setNeeds`, `renameJob`, `addListItem`, …) handed to the editor like canvas changes.
  Values typed into a field reach CodeMirror as `input.type` events, which its history joins into
  one undo step; adding, moving and renaming are `input.canvas` events, a step each. Diagnostics are
  matched to fields by their path (`jobs.build.steps[0].run`).
- **Look**: the cards and React Flow's controls use the design tokens (`canvas/canvas.css`), so the
  canvas follows light and dark.

Pages tell the shell where they are with `usePageInfo`: their breadcrumbs, and the folder to
highlight and expand in the folder tree. The tree follows the ARIA tree pattern (one tab stop,
arrow keys, Home and End) and loads subfolders when one is expanded; the move dialog uses the same
tree as a picker.

All colors come from the tokens in `theme.css`; light and dark share token names, and
`<html data-theme>` picks the values. Text and accent pairs meet WCAG AA.

## Runs

The runs pages (`src/runs/`, `pages/RunsPage.tsx`, `pages/automation/RunsTab.tsx`) read the run
API (`api/runs.ts`):

- **Polling**: a run, its jobs and lists with running runs are read again every two seconds while
  something runs (`refetchInterval`); a run has no live stream of its own.
- **The run page** (`/runs/:id`) fills the window like the editor and loads on demand.
  - It draws the run's own file (`yaml` of the run, so a run stays readable after the automation
    changed) with the canvas, read-only, with each job's status on its card (`RunMark` in
    `canvas/flow.ts`).
  - The chosen job and attempt are in the address.
- **The log** (`src/runs/log/`):
  - *Following*: a finished attempt's log is read page by page (`readLog`). A running one is
    followed over Server-Sent Events (`api/logStream.ts`). That is the one call outside `api`:
    `EventSource` is not `fetch`, and a GET on the same origin with the session cookie needs neither
    the CSRF header nor the middleware.
  - *Reconnecting*: the browser reconnects with `Last-Event-ID`. When it gives up, the log follows
    again from the last chunk known after three seconds. Chunks are kept by sequence, so one that
    arrives twice counts once (`logModel.ts`).
  - *Drawing*: lines are drawn with `@tanstack/react-virtual`, only those in view. Search,
    line links and the download work on the data, not on what is drawn.
  - *Colors*: `ansi.ts` turns SGR escape sequences into styled text segments: the 16 colors with
    the theme's `--log-N` tokens (AA in light and dark), 256 colors and RGB as they are. Every other
    sequence is removed, and a line redrawn with `\r` shows its last state. It never produces
    HTML.

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
