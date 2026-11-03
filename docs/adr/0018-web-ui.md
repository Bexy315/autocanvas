# 0018. Web UI: a React SPA served by the server, with YAML as the editor's source of truth

- Status: Accepted
- Date: 2026-11-01 (accepted 2026-11-03)

## Context

S3 adds the web UI. It needs a visual canvas, a YAML editor, live logs and report views. The
origin's editor is a React app with `@xyflow/react` and dagre and can be harvested. The server
already offers everything through `/api/v1`: a cookie session with a CSRF header for browsers, API
tokens for scripts, problem details and ETags. Automations are YAML files that people may also write
by hand, with comments, and the format keeps `x-…` extension data such as `x-editor` on round trips
([ADR-0007](0007-automation-format-v1.md)). Self-hosters should run as few moving parts as possible.

## Options

How the UI is delivered:

1. **A SPA served by the server** from `wwwroot`. One process, one image, one version, same-origin
   cookies without CORS; the server image needs a Node build stage.
2. **A container of its own** (for example nginx with the static files). Front end and back end
   release separately, but there are two images to run, and a proxy or CORS is needed in between.
3. **Rendered on the server** (Razor or Blazor). There is no npm toolchain, but the canvas would have
   to be rebuilt instead of harvested, and live views need a component model the API does not have.

What the editor edits:

1. **The YAML document**: the canvas changes a `yaml` Document, which keeps comments, order and
   unknown keys, and stores positions under `x-editor.layout`. Hand-written files survive the canvas.
2. **A graph model** serialized to YAML on save. Simpler to edit, but comments and formatting are
   lost on every save, and fields the model does not know need special care.

## Decision

- **The server serves the UI** (option 1). The web app lives in `src/web`, is built with Vite into
  `src/AutoCanvas.Server/wwwroot` (not committed) and is served with a fallback to `index.html`,
  except for `/api` (webhooks included) and `/health`. Self-hosters run one container; the UI always
  matches the API it talks to.
- **The stack follows the origin**, so the editor can be harvested: React 19, TypeScript strict,
  Vite, `@xyflow/react` and `@dagrejs/dagre`, Tailwind 4, Radix primitives, Lucide icons, plus
  TanStack Query and React Router. The UI uses only the public `/api/v1`, typed from
  `openapi/v1.json`.
- **YAML is the source of truth** (option 1): the canvas and a CodeMirror 6 YAML editor both edit
  the same document and stay in sync. The server validates on request with the same Core checks as
  on save, so the browser holds no second copy of the rules.

## Consequences

- One image and one version for server and UI; no CORS, and the cookie session works as it is. CI
  and the Dockerfile gain a Node build; `dotnet` alone still builds and tests the server, which
  serves no UI when `wwwroot` is empty.
- The UI can only do what the public API offers. Endpoints it needs, such as validating without
  saving or a node type catalog, are added to `/api/v1` and are there for scripts as well.
- The canvas must show every valid file, including parts it cannot edit, which it shows read-only.
  Comments and unknown keys survive editing on the canvas.
- Validation goes over the network; the editor debounces and keeps the last diagnostics while a
  request is on its way.
- Revisit if the UI has to be released or scaled independently of the server, or if offline editing
  becomes a requirement.
