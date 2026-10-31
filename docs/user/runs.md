---
sidebar_position: 10
---

# Runs

A **run** is one execution of an automation: its jobs, in the order and under the conditions the
[automation file](automations.md) describes.

:::note Status

Runs can be started, followed and cancelled through the HTTP API. The server hands jobs to
[runners](runners.md), decides every step and keeps the jobs' logs and [reports](reports.md); the reference runner,
schedules and webhooks come in the following steps of this milestone. Until a runner is connected, jobs that
should run stay *running* while they wait for one.

:::

## Starting a run

An automation that allows it (`manual: true` under `triggers`) can be started by hand by anyone
with the **Operator** role on its folder. The run uses the automation's current version and keeps
its own copy of the file: later changes do not affect it, and it stays readable when the automation
is deleted.

With `concurrency: single`, a new run is refused while another run of the automation is still
active.

Runs are numbered per automation, starting at 1 — "run #12 of *Deploy*".

## How a run proceeds

When a run starts and whenever a job finishes, AutoCanvas decides what can happen next, as
described in [How a run proceeds](automations.md#how-a-run-proceeds):

| Job status | Meaning |
|---|---|
| **Pending** | Waits for the jobs it needs |
| **Running** | Handed to a runner, or waiting for one |
| **Succeeded**, **Failed** | Finished |
| **Skipped** | Did not run: its `if` was false, or a job it needs did not end as required; `reason` says which |
| **Cancelled** | Stopped by cancelling the run |

Switch and join jobs are decided by AutoCanvas itself, without a runner; a switch shows the case it
chose. Each try to run a job is an **attempt**; a job with `retry` can have several.

The run itself is **Running** until every job has finished, and then **Succeeded**, **Failed** or
**Cancelled**.

Expressions in a run read the values described in [Expressions](expressions.md): `trigger.*`,
`run.*`, the [variables and secrets](variables-and-secrets.md) of the instance, the
automation's folders and its file, the outputs of earlier steps and of the jobs a job needs.

## How a job proceeds

A runner takes a job, prepares its workspace, image and services, and then gets its steps **one at
a time**. Before each step the server evaluates the step's `if` and every expression in it, with
the outputs of the steps before it; a step whose condition is false is skipped and never sent.
After a failed step the remaining steps are skipped, unless the failed step has
`continue_on_error: true`. The job's result follows from its steps: it fails if a step failed
(and may not), if the runner could not execute a step, or if preparation failed.

When the job is decided, its `outputs` are evaluated and stored; jobs that need it read them as
`${{ jobs.ID.outputs.NAME }}`. An output that was never set reads as empty.

Secrets reach the runner only with the steps that use them, and the runner masks them in
everything it sends back. The server masks them again: step and job outputs that contain a secret
are stored with `***` in its place. Environment entries of a job that read a secret are given to
each step instead of to the job as a whole.

## Time limits

A job's `timeout_minutes` (else the automation's, else 60 minutes) counts from the moment a runner
takes it. When it is exceeded, the runner is told to stop the job and the job **fails**; if the
runner does not report back within the lease (`Queue:LeaseDuration`), the job fails without it. A
step's `timeout_minutes` is enforced by the runner; such a step counts as failed.

## Logs

Everything a job writes — while its runner prepares it, during each step, and while it cleans up —
is kept as the job's **log**, per attempt. Secrets are masked twice: by the runner before it sends
a line, and by the server before it stores it. A line longer than 16 KiB is cut, and an attempt
keeps at most `Logs:MaxBytesPerJob` (64 MiB by default) of log text; after that the log ends with
a note that later lines were dropped.

Runners send the log in numbered chunks. A chunk that arrives twice — after the runner reconnected
— is kept once; a chunk that never arrives leaves a gap, which the API reports as `missing`.

Logs are read with Viewer, like the run:

- `GET /api/v1/runs/{id}/jobs/{key}` shows a job with its outputs and every attempt with its steps;
  each attempt's `id` names its log.
- `GET /api/v1/runs/{id}/attempts/{attemptId}/log?after=N&limit=M` returns the chunks after
  sequence `N` (all from the start without `after`), at most `M` of them (default 100). Read on with
  `next` until `isComplete` is true.
- `GET /api/v1/runs/{id}/attempts/{attemptId}/log/stream` follows the log live as
  [Server-Sent Events](https://html.spec.whatwg.org/multipage/server-sent-events.html): a `chunk`
  event per chunk, whose `id` is its sequence, then an `end` event once the attempt has finished.
  A reader that reconnects sends `Last-Event-ID` (browsers do this by themselves) and continues
  where it left off.

```text
id: 3
event: chunk
data: {"sequence":3,"stepIndex":0,"lines":[{"time":"2026-11-01T10:00:02Z","source":"Stdout","text":"Build succeeded"}]}

event: end
data: {"isTruncated":false,"missing":[]}
```

## Cancelling a run

An **Operator** can cancel a running run. Every job that has not finished is cancelled: jobs
waiting for a runner at once, running jobs by telling their runner to stop the current step (it
gets a few seconds to end gracefully), collect reports and clean up. `finally` jobs with
`on_cancel: true` still run afterwards, so the run finishes once they have. Cancelling a run that
has already finished changes nothing.

## Who may do what

| Role on the folder | May |
|---|---|
| **Viewer** | list and read runs |
| **Operator** | everything a Viewer may, and start and cancel runs |

Runs belong to the folder of their automation. Deleting a folder deletes the runs left in it;
that is only possible once its automations have been deleted or moved.

## Using the API

| Request | Does |
|---|---|
| `POST /api/v1/automations/{id}/runs` | start a run by hand |
| `GET /api/v1/automations/{id}/runs` | the automation's runs, newest first |
| `GET /api/v1/runs/{id}` | one run with its jobs |
| `GET /api/v1/runs/{id}/jobs/{key}` | one job with its outputs, attempts and steps |
| `GET /api/v1/runs/{id}/attempts/{attemptId}/log` | a part of an attempt's log |
| `GET /api/v1/runs/{id}/attempts/{attemptId}/log/stream` | follow an attempt's log live |
| `GET /api/v1/runs/{id}/reports` | the run's test and coverage reports with their totals (see [Reports](reports.md#reading-reports)) |
| `POST /api/v1/runs/{id}/cancel` | cancel a run |

```json
{
  "id": "01a1…",
  "number": 12,
  "automationName": "deploy",
  "automationVersion": 4,
  "state": "Running",
  "trigger": "Manual",
  "triggeredByName": "ada",
  "jobs": [
    { "key": "build", "name": "Build", "status": "Running", "attempts": 1 },
    { "key": "nightly", "name": "nightly", "status": "Skipped", "reason": "…" },
    { "key": "deploy", "name": "Deploy", "status": "Pending", "attempts": 0 }
  ]
}
```

Starting and cancelling runs is recorded in the [audit log](../technical/audit.md).
