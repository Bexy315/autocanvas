---
sidebar_position: 1
---

# Runner protocol v1

Runners execute jobs for the AutoCanvas server. This page specifies how they talk to it, so that
runners can be written in any language. The contract is the set of Protocol Buffers files in
`proto/autocanvas/runner/v1/` (package `autocanvas.runner.v1`); this page explains how to use them.
The words MUST, SHOULD and MAY are used as in RFC 2119.

:::note Specification

The server implements registration, the control stream's handshake, heartbeats, offers and
draining; steps, results and logs follow in S2. The reference runner is being built.

:::

## Principles

- **The runner connects out.** Runners open all connections; they need no inbound ports and can
  run behind NAT or firewalls.
- **The server decides, the runner executes.** The server evaluates every expression and every
  condition ([ADR-0008](../adr/0008-server-resolves-steps.md)). A runner receives fully resolved
  jobs and steps, one step at a time, and never sees the automation file, variables it does not
  need, or secrets of steps it has not reached.
- **gRPC over HTTP/2** ([ADR-0004](../adr/0004-runner-protocol-grpc-spec-first.md)) on a port of
  the server's own, separate from the HTTP API (by default `5081`,
  [ADR-0017](../adr/0017-runner-endpoint-and-credentials.md)). It also speaks HTTP/2 without TLS
  (prior knowledge) for local networks; TLS SHOULD be used everywhere else.

## Services

| Service | Call | Purpose |
|---|---|---|
| `RegistrationService` | `Register` (unary) | Exchange a one-time registration token for a runner id and credential. |
| `RunnerService` | `Connect` (bidirectional stream) | The control stream: offers, steps, progress, logs, results, cancellation. |
| `RunnerService` | `UploadReport` (client stream) | Upload one report file of a job. |

Every `RunnerService` call MUST carry the credential as metadata
`authorization: Bearer <credential>`. A rejected credential (`UNAUTHENTICATED`) means the runner
was deleted or its credential revoked; the runner MUST stop and MUST NOT retry with it.

## Registration

1. An administrator creates a registration token in the UI.
2. The runner calls `Register` with the token, its name and its capabilities.
3. The server returns `runner_id` and `credential`. The runner stores the credential securely; it is
   never shown again. The token is consumed.

## The control stream

### Connecting

The runner opens `Connect` and MUST send `Hello` as its first message: protocol version,
capabilities and the ids of jobs it is still working on (after a reconnect). The server answers
with `Welcome`, which sets the heartbeat interval and lease duration.

The runner MUST send `Heartbeat` at the heartbeat interval, listing its active jobs. A job's lease
is renewed by any heartbeat naming it; when a lease runs out, the server treats the job as lost
(it fails or retries it according to the automation).

When the stream breaks, the runner SHOULD reconnect with exponential backoff (1 s doubling to 60 s,
with jitter) and keep executing its jobs meanwhile. In its next `Hello` it lists them; the server
either lets them continue or answers with `CancelJob`. Messages that could not be sent while
disconnected SHOULD be sent after reconnecting; log chunks carry sequence numbers so duplicates are
dropped.

`Goodbye` closes the stream on purpose. If `reconnect` is false the runner MUST NOT reconnect.

### Running a job

```text
server                                    runner
  │ JobOffer(job) ───────────────────────────▶│
  │◀─────────────────────────── JobAccepted    │  (or JobRejected)
  │                                            │  prepare: workspace, checkouts,
  │◀──────────────────── LogChunk(job_setup)…  │  image, services
  │◀─────────────────────────── JobPrepared    │
  │ RunStep(step 0) ─────────────────────────▶│
  │◀─────────────────────────── StepStarted    │
  │◀──────────────────── LogChunk(step 0)…     │
  │◀────────────────── StepCompleted(outputs)  │
  │ RunStep(step 2) ─────────────────────────▶│  (step 1 skipped by its condition)
  │   …                                        │
  │ CompleteJob ─────────────────────────────▶│
  │                                            │  collect reports → UploadReport
  │                                            │  clean up workspace and services
  │◀─────────────────────────── JobFinished    │
```

- **Offer.** `JobOffer` contains the resolved job: image, checkouts (with short-lived
  credentials), services, environment and report specifications. The server only offers jobs whose
  labels, executor and node types the runner has announced. The runner MUST answer with
  `JobAccepted` or `JobRejected` within the lease duration. It SHOULD reject rather than queue when
  it is at capacity.
- **Prepare.** After accepting, the runner creates a fresh workspace, clones the checkouts in
  order, pulls the image, starts the services and waits for their `ready_command`. Output of this
  phase goes into log chunks with `job_setup = true`. It then sends `JobPrepared`, with `error` set
  if preparation failed.
- **Steps.** The server sends `RunStep` messages one at a time. For each, the runner sends
  `StepStarted`, streams logs, and sends `StepCompleted` with outcome, exit code and outputs. It
  MUST NOT start the next step before the server sends it. Steps whose condition is false are never
  sent; step indexes can therefore skip numbers.
- **Complete.** `CompleteJob` means there are no more steps — because all ran, a step failed, or
  preparation failed. The runner collects the job's reports (also after failures), uploads them,
  removes the workspace and services, and sends `JobFinished`.

The job's result (succeeded or failed) is decided by the server from the step outcomes and the
automation's rules, such as `continue_on_error`. The runner only reports what happened.

### Executing steps

- **Script steps** run `script` with the given `shell` in `working_directory` (relative to the
  workspace). Environment: the job's `env`, then the step's `env`, then `AUTOCANVAS_OUTPUT` — the
  path of an empty file the runner creates per step. After the script exits, the runner reads that
  file as `name=value` lines and returns them as outputs. Exit code 0 is
  `STEP_OUTCOME_SUCCEEDED`, anything else `STEP_OUTCOME_FAILED`.
- **Node steps** execute the node type named in `type` with `inputs`. A runner announces the node
  types it implements in its capabilities and receives no others.
- **Timeouts.** When a step exceeds its `timeout`, the runner stops it and reports
  `STEP_OUTCOME_TIMED_OUT`.
- **Errors of the runner itself** (shell missing, container runtime gone) are reported as
  `STEP_OUTCOME_ERRORED`, so they can be told apart from a failing script.

### Cancellation

On `CancelJob` the runner stops the running step — first gracefully (SIGTERM, then SIGKILL after
10 seconds, or the platform's equivalent) — reports it as `STEP_OUTCOME_CANCELLED`, and then
continues as on `CompleteJob`: collect reports, clean up, send `JobFinished`.

### Draining

A runner that wants to shut down sends `Draining`. The server sends no more offers; running jobs
finish normally. After its last `JobFinished` the runner closes the stream.

## Logs

- Lines are sent in `LogChunk` batches, each with the job's next `sequence` number (starting at 0,
  shared by setup and step logs of a job). A runner SHOULD send a chunk at least every 500 ms while
  output arrives, and keep chunks below 64 KiB.
- `text` holds one line without its line terminator. Very long lines MAY be split.
- Every value in a step's `mask_values` MUST be replaced by `***` in log lines, outputs and error
  messages before they are sent. Checkout passwords are masked the same way. The server masks again
  on receipt; runner-side masking keeps secrets off the wire.

## Reports

After the steps, the runner looks for files matching each `ReportSpec`'s `paths` (globs relative to
the workspace) and uploads every match with `UploadReport`: first a `ReportHeader`, then the content
in chunks of at most 1 MiB. Files not found are a warning in `JobFinished`, not a failure. Reports
are uploaded before `JobFinished`.

## Versioning

The protocol version is `1.<minor>`. Within major version 1, changes are additive only: new
messages, new optional fields, new enum values. CI enforces wire and JSON compatibility with
`buf breaking`. Each side MUST ignore fields and `oneof` cases it does not know. A runner announces
the highest version it speaks in `Hello`; the server answers with the version it uses on the stream.
A change that is not additive becomes package `autocanvas.runner.v2`, served alongside v1.
