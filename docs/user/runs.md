---
sidebar_position: 10
---

# Runs

A **run** is one execution of an automation: its jobs, in the order and under the conditions the
[automation file](automations.md) describes.

:::note Status

Runs can be started, followed and cancelled through the HTTP API, and AutoCanvas decides which jobs
start, are skipped or wait. Runners that execute the jobs, schedules and webhooks come in the
following steps of this milestone; until then, jobs that should run stay *running* while they wait
for a runner.

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
`run.*`, and the [variables and secrets](variables-and-secrets.md) of the instance, the
automation's folders and its file.

## Cancelling a run

An **Operator** can cancel a running run. Every job that has not finished is cancelled; `finally`
jobs with `on_cancel: true` still run afterwards, so the run finishes once they have. Cancelling a
run that has already finished changes nothing.

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
