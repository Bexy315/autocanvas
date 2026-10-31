---
sidebar_position: 11
---

# Runners

A **runner** is a program on a machine of yours that executes jobs for the AutoCanvas server. It
connects out to the server, so it needs no open ports itself. The server offers it the queued jobs
it can take, sends their steps one at a time, and decides from what the runner reports whether a
job succeeded.

:::note Status

The server side is in place: runners register, connect, are offered jobs and get their steps one
at a time (see [How a job proceeds](runs.md#how-a-job-proceeds)). The reference runner that
executes them, logs and reports follow in the next steps of S2. Until then
the HTTP API below is how runners are managed; the web UI comes with S3.

:::

Managing runners is for **admins**.

## Registering a runner

1. Create a registration token:

   ```sh
   curl -X POST https://autocanvas.example.org/api/v1/runner-registration-tokens \
     -H "Authorization: Bearer $AUTOCANVAS_TOKEN" -H "Content-Type: application/json" \
     -d '{"description": "build box in the basement"}'
   ```

   The answer contains the token (`acrt_…`) once. It works for one registration and expires after
   24 hours unless you give another `expiresAt`, at most 30 days away.
2. Pass the token to the runner's `register` command together with the server's runner address
   (by default port `5081`, see [Operations](../technical/operations.md#runners)). The runner
   exchanges it for its own credential and keeps that.

A registration token that is no longer needed can be deleted
(`DELETE /api/v1/runner-registration-tokens/{id}`); a runner that already registered with it stays.

## What a runner can do

When it connects, a runner announces what it can do, and the server offers it only jobs that fit:

| Announced | A job needs it when |
|---|---|
| **Labels**, such as `linux` or `gpu` | its `runs_on` lists the label — a runner must have all of them |
| **Executor**: container or host | it has an `image` or `services` (needs the container executor) |
| **Node types**, such as `http_request` | one of its steps uses the node type |
| **Jobs at a time** | — the runner gets no more jobs than it runs at once |

`GET /api/v1/runners` lists every runner with what it last announced, when it was last seen and
whether it is **online** (heard from within the last two heartbeats).

## Pausing, renaming and removing

- **Pause** a runner, for example before maintenance, with
  `PATCH /api/v1/runners/{id}` and `{"isPaused": true}`. It stays connected and finishes the jobs
  it has, but is offered no new ones; `{"isPaused": false}` resumes it. The same request renames it
  with `"name"`. Names are unique, regardless of case.
- **Delete** a runner with `DELETE /api/v1/runners/{id}`. Its credential stops working at once; a
  connected runner is told not to reconnect. Jobs it still held are offered to other runners or
  fail, as their `retry` settings say.

Every change is in the [audit log](../technical/audit.md#actions).

## When a runner disappears

A runner holds each of its jobs with a **lease** that its heartbeats renew. If the runner loses its
connection, it keeps working and reconnects; jobs it still holds then continue. If it stays away
longer than the lease (one minute by default), its jobs are given up: an offered job goes to another
runner, a running one fails and is retried if its `retry` allows. When the server itself stops,
connected runners are told to reconnect a few seconds later.
