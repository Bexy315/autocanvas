# 0010. Support PostgreSQL and SQLite behind provider ports

- Status: Accepted
- Date: 2026-10-30
- Supersedes: [0005](0005-own-postgres-job-queue.md)

## Context

AutoCanvas should be easy to host in very different places: a single container on a small
machine, a team server, several server instances behind a load balancer. ADR-0005 made PostgreSQL
the only database and designed the job queue around `FOR UPDATE SKIP LOCKED` and `LISTEN/NOTIFY`.

PostgreSQL remains the best fit for production and scale-out, but requiring it makes the smallest
setup heavier than it needs to be, and the maintainer wants to be able to add further databases
later. That is cheap to plan for now and expensive to retrofit once migrations, queries and tests
assume one database.

## Options

1. **PostgreSQL only** (ADR-0005) — simplest, full use of PostgreSQL features; every installation
   needs a PostgreSQL server; adding a database later means auditing all data access.
2. **Provider-neutral design, PostgreSQL implemented** — the code is shaped for more databases, but
   only one is built and tested. Cheap now; leaks into the "neutral" code go unnoticed.
3. **PostgreSQL and SQLite from the start** — EF Core for the model and ordinary queries;
   database-specific work behind narrow ports with one implementation per provider; one migration
   set per provider; every persistence test runs on both.
4. **PostgreSQL and MySQL/MariaDB** — covers hosts that only offer MySQL, but keeps the external
   database requirement for every installation.

## Decision

Option 3. SQLite gives the zero-dependency setup (one container, one file) for single-node
installations; PostgreSQL is the recommendation for production and the only option for several
server instances. Building both from the first migration keeps the abstraction honest, and a later
provider (MySQL, SQL Server) follows the same pattern.

Rules that make this work:

- No SQL outside the `AutoCanvas.Persistence.<Provider>` projects.
- The shared model uses only types both providers handle: JSON as text, timestamps as UTC in a
  sortable form, case-insensitive uniqueness through normalized columns instead of collations.
- Anything that needs database-specific behavior is a port in the application layer with an
  implementation per provider. The first such port is the **job queue** (S2): PostgreSQL claims jobs
  with `SKIP LOCKED` and wakes the dispatcher with `LISTEN/NOTIFY`; SQLite uses a single-node
  dispatcher that serializes claims in process.
- Migrations are generated for both providers together and live in their provider project.

The rest of ADR-0005 still holds: the queue is AutoCanvas's own, with leases renewed by runners and
no Hangfire or message broker.

## Consequences

- Two migration sets to maintain; the `add-migration` skill makes generating both one task.
- CI runs the persistence and API tests twice (PostgreSQL via Testcontainers, SQLite on a file).
- PostgreSQL-only features (for example `jsonb` queries) are unavailable outside a port.
- SQLite installations are single-node by design; the operations docs say when to move to
  PostgreSQL. A migration path between providers is not part of S1.
