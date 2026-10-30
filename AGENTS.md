# AGENTS.md

Guide for coding agents (and humans) working in this repository. It is normative: follow it unless
the maintainer says otherwise in the conversation. Area-specific rules live in nested `AGENTS.md`
files (e.g. `src/AutoCanvas.Core/AGENTS.md`); the nearest one wins where they differ.

## What this is

AutoCanvas is a self-hosted automation and CI/CD engine: pipelines as YAML in a repository or built
on a visual canvas, executed by lightweight runners, with test reports as a first-class feature.
MIT-licensed. Pre-alpha — the current milestone plan is in [`docs/plans/`](docs/plans/).

## Layout

| Path | Contents |
|---|---|
| `src/AutoCanvas.Core/` | Pipeline model, parsing, validation, expressions, DAG. Pure code, no I/O. |
| `src/AutoCanvas.Reports/` | Test and coverage report parsers |
| `src/AutoCanvas.Server/` | ASP.NET Core host: HTTP API, health, configuration |
| `openapi/` | Committed OpenAPI document of the HTTP API, checked by a test |
| `benchmarks/` | BenchmarkDotNet benchmarks of Core's hot paths; baselines in `docs/technical/performance.md` |
| `tests/<Project>.Tests/` | Tests for `src/<Project>`; coverage is measured per pair |
| `proto/` | Runner protocol (Protocol Buffers, `buf.yaml` at the root) |
| `schema/` | JSON Schema of the automation format, with valid and invalid examples |
| `docs/user/` | User documentation (how to use AutoCanvas) |
| `docs/technical/` | Architecture, security model, operations, dependencies |
| `docs/protocol/` | Runner protocol specification (prose around the `.proto` files) |
| `docs/adr/` | Architecture decision records |
| `docs/plans/` | Milestone plans; the active one says what to work on |
| `docs-site/` | Docusaurus app that renders `docs/` (except `plans/`) to GitHub Pages |
| `build/` | Build and CI support files |
| `.claude/skills/` | Task playbooks — any agent can read them (see below) |

Planned areas (persistence, runner, web UI) appear with their milestone; see the active plan.

## Commands

Run from the repository root. These are exactly what CI runs.

```sh
dotnet tool restore
dotnet format --verify-no-changes   # style + analyzers; `dotnet format` fixes what it can
dotnet build -c Release             # warnings are errors
dotnet test -c Release              # ≥ 75 % line coverage per project, else exit code 14
dotnet nuget-license --input AutoCanvas.slnx --include-transitive --allowed-license-types build/allowed-licenses.json
```

Protocol (needs the `buf` CLI, e.g. `npx @bufbuild/buf`):

```sh
buf lint
buf format --diff --exit-code     # `buf format -w` fixes it
buf breaking --against '.git#branch=main'
```

Documentation site (Node 24), from `docs-site/`:

```sh
npm ci
npm run typecheck
npm run build      # broken links, anchors and images fail the build
npm run start      # local preview with hot reload
```

Benchmarks (not part of CI; run before and after performance work, see `docs/technical/performance.md`):

```sh
dotnet run -c Release --project benchmarks/AutoCanvas.Benchmarks -- --filter '*' --job short
```

Coverage reports are written to `artifacts/coverage/`. For a readable summary:
`dotnet reportgenerator -reports:'artifacts/coverage/*.xml' -targetdir:artifacts/coverage/report -reporttypes:TextSummary`.

## Coding rules

- **No warnings, no blanket suppressions.** Fix the cause. A suppression is allowed only for a
  single rule, as narrowly scoped as possible, with a comment saying why.
- **Public API is documented** with XML docs that explain intent, not restate the name.
- **Core stays pure**: no file system, network, database, clock or randomness in `AutoCanvas.Core`.
  Effects live in the host projects and are passed in.
- **Tests come with behavior.** Every change in behavior has a test that fails without it. Test
  names are sentences (`Unknown_names_do_not_resolve`). Prefer table-driven theories for parsers
  and validators.
- **Dependencies are a decision.** New packages need an MIT-compatible license
  (`build/allowed-licenses.json`), a pinned version in `Directory.Packages.props`, a line in
  `docs/technical/dependencies.md`, and an ADR if they shape the architecture.
- **Match the surrounding code**: naming, comment density, idioms. Comments explain *why*.
- **English** for code, comments, docs, commits and PRs.

## Definition of Done

A change is done when all of these hold — the PR template repeats them:

1. All commands above pass locally.
2. New or changed behavior is covered by tests.
3. Docs are updated where the change is visible: user docs, technical docs, XML docs
   (skill: `docs-update`).
4. Architectural decisions have an ADR (skill: `write-adr`).
5. The active plan in `docs/plans/` reflects what was done or changed.

## Workflow

- Never commit to `main`; it is protected. One branch per change, named `<type>/<short-topic>`.
- Commits and PR titles follow Conventional Commits (`feat:`, `fix:`, `docs:`, `build:`, `ci:`,
  `refactor:`, `test:`, `chore:`). PRs are squash-merged, so the PR title becomes the commit.
- Open PRs with `gh pr create`, wait for green checks, and leave merging to the maintainer.
- Before starting, check the current state (`git status`, `git log`, open PRs): other agents may
  work in this repository at the same time.
- When something is unclear or a plan does not fit reality, stop and ask instead of guessing.

## Skills

Playbooks for recurring tasks live in `.claude/skills/<name>/SKILL.md`. Claude Code loads them
automatically; other agents should read the matching file before starting such a task.

| Skill | Use it when |
|---|---|
| `quality-gates` | running or debugging format, build, test, coverage or license checks |
| `write-adr` | recording an architectural decision |
| `docs-update` | deciding which docs a change touches and updating them |
| `harvest` | moving code over from the origin project |
