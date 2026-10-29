# 0008. The server resolves steps, runners execute them

- Status: Accepted
- Date: 2026-10-30

## Context

Automations contain expressions (`${{ steps.build.outputs.digest }}`, `if:` conditions) whose
values are only known while a job runs: a step's inputs can depend on the outputs of the step
before it. Someone has to evaluate them between steps.

The origin engine sent the whole job — definition, all variables and all secrets — to the runner,
which evaluated expressions itself. AutoCanvas wants runners in other languages
([ADR-0004](0004-runner-protocol-grpc-spec-first.md)), and every runner implementation would then
need an identical copy of the expression language.

## Options

1. **Runner evaluates** — no round trip per step, but the expression language is implemented once
   per runner language, and all secrets of a job reach the runner up front.
2. **Mixed** — the server resolves what it knows before the job starts; the runner substitutes step
   outputs and evaluates step conditions. Fewer round trips, but part of the language still lives
   in every runner.
3. **Server resolves step by step** — the runner reports each step's outcome and outputs; the
   server evaluates the next step's condition and expressions and sends it fully resolved, or
   skips it.

## Decision

Option 3. Runners never evaluate expressions or conditions. The server sends one resolved step at
a time over the control stream (`RunStep`), and decides the job's result from the reported step
outcomes.

## Consequences

- The expression language exists exactly once, in `AutoCanvas.Core`, and behaves identically on
  every runner.
- Writing a runner is simpler: execute a script or node type with given inputs, report the result.
- Secrets reach the runner only with the step that uses them, and the step's `mask_values` tell
  the runner what to mask.
- Each step costs one message round trip on the already open stream — milliseconds, negligible
  against step durations. A runner cut off from the server cannot proceed to the next step; it
  keeps the running step going and resumes after reconnecting.
