---
name: quality-gates
description: Run AutoCanvas's quality gates locally exactly as CI does (format, build, tests with the 75 % coverage gate, dependency licenses) and diagnose failures. Use before opening or updating a PR, and whenever a CI check fails.
---

# Quality gates

CI (`.github/workflows/ci.yml`) is the source of truth. This skill mirrors it locally so failures
show up before a push.

## Run everything

From the repository root, in this order — each step assumes the previous one passed:

```sh
dotnet tool restore
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
dotnet nuget-license --input AutoCanvas.slnx --include-transitive --allowed-license-types build/allowed-licenses.json
```

If anything under `docs/` or `docs-site/` changed, also run the docs gate from `docs-site/`:

```sh
npm ci && npm run typecheck && npm run build
```

Report the result per step. Do not claim the gates pass unless every command exited with 0.

## Reading failures

| Symptom | Meaning | Fix |
|---|---|---|
| `dotnet format` lists files or diagnostics | Formatting or an analyzer rule that `format` checks | Run `dotnet format`, then re-check; analyzer findings it cannot fix need a code change |
| Build error `CAxxxx` / `MAxxxx` / `IDExxxx` | Analyzer rule (warnings are errors) | Fix the code. Suppress only one rule, narrowly, with a justification comment — never in `Directory.Build.props` |
| Build error `CS1591` | Public member without XML docs | Write a doc comment that explains intent |
| `NU1004` / lock file errors | `packages.lock.json` out of date | `dotnet restore --force-evaluate`, commit the lock files |
| `NU1507` | Extra package sources | The repo's `nuget.config` must keep `<clear />`; do not add feeds |
| Test run exit code **14**, all tests passed | Line coverage of a project fell below 75 % | Add tests for the uncovered code (see below). Do not lower the threshold |
| Test run exit code **2** | At least one test failed | Read the failing test output |
| Test run exit code **8** | A test project found no tests | Add tests or remove the empty project |
| Docs build: `MDX compilation failed` or `Broken link` | A relative link points to a missing page or anchor, or a link leaves `docs/` | Fix the link. Link only within `docs/`; mention repository files (e.g. `build/…`) as code, not links. `docs/plans/` is not published, so do not link to it from published docs |
| License check lists a package as not allowed | Dependency license is not on the allow list | Pick another package, or write an ADR before extending `build/allowed-licenses.json` |

## Finding uncovered code

Coverage files are in `artifacts/coverage/<Project>.coverage.cobertura.*.xml`. Delete the folder
first so old runs do not mix in:

```sh
rm -rf artifacts/coverage
dotnet test -c Release
dotnet reportgenerator -reports:'artifacts/coverage/*.xml' -targetdir:artifacts/coverage/report -reporttypes:'TextSummary;Html'
cat artifacts/coverage/report/Summary.txt
```

`Summary.txt` lists coverage per class; `index.html` shows uncovered lines. Cover behavior, not
lines: a test should assert something a user of the API would care about.

## Rules

- Never weaken a gate (threshold, analyzer level, `TreatWarningsAsErrors`, allow list) to get a
  change through. If a gate seems wrong, raise it with the maintainer and record the outcome in an
  ADR.
- `[ExcludeFromCodeCoverage]` is allowed only for generated code and trivial entry points, with a
  `Justification`.
