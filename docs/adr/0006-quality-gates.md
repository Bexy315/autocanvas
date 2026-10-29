# 0006. Enforce quality gates in CI from the first commit

- Status: Accepted
- Date: 2026-10-29

## Context

AutoCanvas is developed by the maintainer together with coding agents, and it aims for a higher
quality bar than the origin engine. Agents produce code quickly; without hard gates, inconsistency
and untested code accumulate faster than review can catch them. Gates added later are expensive,
because existing code has to be brought up to them first.

## Decision

Every pull request into the protected `main` branch must pass these checks, and they exist before
any functional code:

| Gate | Rule |
|---|---|
| Formatting and style | `dotnet format --verify-no-changes`; code style enforced in the build |
| Compiler and analyzers | Warnings are errors, `AnalysisLevel=latest-all`, Meziantou.Analyzer |
| Code documentation | XML docs required on public API of libraries (CS1591 is an error) |
| Tests and coverage | All tests pass; ≥ 75 % line coverage **per project** (coverlet) |
| Dependencies | Versions pinned centrally, lock files in locked mode, nuget.org only, MIT-compatible licenses only |
| Secrets | gitleaks over the full history |
| History | Conventional Commit PR titles, squash merges, linear history |
| Documentation | Docs site builds with broken links and anchors treated as errors |
| Static analysis | CodeQL — enabled once the repository is public (needs GitHub Advanced Security while private) |

Web (TypeScript strict, linter, formatter, ≥ 75 % coverage, Playwright smoke tests) and Rust
(`clippy -D warnings`, `rustfmt`, coverage) gates are added with the first code in those languages.

Exceptions are narrow by design: a single analyzer rule may be suppressed for a single scope with a
written justification. Relaxing a gate itself requires a new ADR.

## Consequences

- Early changes are slower; later changes are safer, and agents get fast, unambiguous feedback.
- 75 % per project is a floor, not a target; pure code such as Core aims higher.
- Coverage below the threshold surfaces as test exit code 14 from Microsoft.Testing.Platform —
  documented in the `quality-gates` skill.
