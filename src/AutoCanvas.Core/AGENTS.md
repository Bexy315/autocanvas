# AGENTS.md — AutoCanvas.Core

The domain core: pipeline model, parsing, validation, expressions and the decisions of
orchestration (what may run next, given the current state). Everything else in AutoCanvas builds on
it, and it has the highest bar for correctness and performance.

- **Pure code only.** No I/O, no `DateTime.Now`/`Guid.NewGuid()`/`Random`, no static mutable state,
  no logging. If something needs time, IDs or data from outside, take it as a parameter or an
  interface the host implements.
- **No dependencies** beyond the BCL without an ADR. Parsing libraries (e.g. YAML) are the expected
  exception and get one.
- **Immutable models.** Prefer `sealed record` and read-only collections; validation returns
  diagnostics rather than throwing for user mistakes. Exceptions are for programming errors.
- **Diagnostics are user-facing.** Every validation message names the location (file, path in the
  document) and says how to fix it. They end up in the UI and in CLI output.
- **Coverage target is higher than the gate**: aim for ≥ 90 % here; the code is pure, so there is
  no excuse for untested branches.
- **Mind allocations** on hot paths (parsing, expression evaluation). Measure with the benchmark
  project (`benchmarks/`, arrives in S0 step 6) before and after optimizing; do not optimize without a number.
