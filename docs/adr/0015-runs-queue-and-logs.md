# 0015. Store runs with a copy of their definition, queue jobs per provider, keep logs as chunks in the database

- Status: Proposed
- Date: 2026-10-31

## Context

S2 makes automations run. The protocol is fixed ([ADR-0004](0004-runner-protocol-grpc-spec-first.md),
[ADR-0008](0008-server-resolves-steps.md)): the server offers a job to a runner over one control
stream, leases it while heartbeats renew it, sends one resolved step at a time and receives log
chunks (≤ 64 KiB, numbered per job attempt) and reports. The queue is AutoCanvas's own, with
`SKIP LOCKED` and `LISTEN/NOTIFY` on PostgreSQL and a single-node dispatcher on SQLite
([ADR-0010](0010-database-providers.md)). Core decides what happens next from a `RunState`
(`RunPlanner`, `StepPlanner`, `RunOutcome`). Automations are versioned and can be deleted with all
versions; runs must stay readable afterwards. Logs must be redacted, survive restarts, work with
several PostgreSQL servers and stream live to readers.

## Options

For the definition a run uses:

1. **Reference the automation version** — no duplication, but deleting the automation would
   delete or orphan its runs.
2. **Copy the YAML into the run** — a few kilobytes per run; the run is self-contained.

For logs:

1. **Chunks in the database** — one row per received chunk; same on both databases and on several
   servers, one backup, retention is a delete; the database grows with log volume.
2. **Files in the data directory** — small database, but several servers need a shared volume and
   backups need the files too.
3. **A storage port with the database first** — flexible, but an abstraction without a second
   implementation yet.

## Decision

- A **run** keeps a copy of the automation file it runs, the automation's id, name and version
  number, its trigger (kind and data such as a webhook body) and who started it. It survives the
  automation's deletion. Runs are numbered per automation (`#1`, `#2`, …) for people; ids stay
  UUIDs.
- **Jobs** are stored per run with their Core status; every **attempt** of a job is a row of its
  own, because the protocol's `job_id` names one attempt. Attempts carry the queue state: when they
  may start, which runner holds them and until when (lease). **Steps** are stored per attempt.
- The **queue** is a port with one implementation per provider: PostgreSQL claims with
  `FOR UPDATE SKIP LOCKED` and wakes dispatchers with `LISTEN/NOTIFY`; SQLite serializes claims
  inside the one server process. Expired leases are swept and retried or failed as the automation
  says.
- **Logs are stored as chunks in the database**, as the runner sent them but redacted again by the
  server, unique per attempt and sequence number so duplicates are dropped and gaps are visible.
  Readers get a range of a job's log or follow it live through Server-Sent Events.
- Runs, with their logs and reports, are kept **90 days** by default, and the newest 20 runs of
  each automation always.

## Consequences

- A run can be explained and re-read without the automation; the copy is also what `ResolveAsync`
  and the planners read.
- Log-heavy installations grow their database; the retention setting is the lever. If that becomes
  a problem, a storage port for files or object storage can be added behind the same API.
- Artifacts are not covered: they are large files and get their own decision with the protocol RPC
  that uploads them.
