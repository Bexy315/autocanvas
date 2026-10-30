# 0005. Dispatch jobs through an own PostgreSQL queue

- Status: Superseded by [0010](0010-database-providers.md)
- Date: 2026-10-29

## Context

The server must hand ready jobs to runners: match them by labels, lease them so that exactly one
runner works on a job, detect lost runners, retry or fail, and keep the latency between "job is
ready" and "runner starts" low. The origin engine used Hangfire with PostgreSQL storage for
background work, including parts of dispatch.

PostgreSQL is already the only required infrastructure (no Redis, no message broker), and that
should stay true for simple self-hosting.

## Options

1. **Hangfire** — mature background-job framework. Its model is "the server executes a method",
   not "a remote runner leases a job by labels"; polling intervals add latency; it is a large
   dependency for what remains.
2. **A message broker** (RabbitMQ, NATS) — good dispatch semantics, but a second piece of
   infrastructure for every installation.
3. **An own queue table in PostgreSQL** — jobs are claimed with `SELECT … FOR UPDATE SKIP LOCKED`,
   leases have an expiry that runners renew, and `LISTEN/NOTIFY` wakes the dispatcher instead of
   polling.

## Decision

Option 3. The job queue is part of the server, designed for the runner protocol's lease model
([ADR-0004](0004-runner-protocol-grpc-spec-first.md)). Time-based triggers (cron schedules) are
handled by the server's own scheduler writing into the same queue.

## Consequences

- No Hangfire dependency; dispatch semantics (leases, retries, cancellation, label matching) are
  under our control and testable against a real PostgreSQL with Testcontainers.
- We own correctness of a concurrency-sensitive component: it needs focused tests (concurrent
  claims, expired leases, server restarts) and a benchmark for dispatch latency.
- Scale-out to several server instances works through the database, with no extra coordination.
