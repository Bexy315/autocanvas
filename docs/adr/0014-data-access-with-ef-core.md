# 0014. Data access with EF Core, one project per database provider

- Status: Accepted
- Date: 2026-10-30 (accepted 2026-11-01)

## Context

[ADR-0010](0010-database-providers.md) requires the server to run on PostgreSQL and SQLite from
the start, with database-specific work behind ports and migrations for both providers. The server
needs a data-access layer for ordinary CRUD and permission queries, and a way to keep two schemas
in step. The origin engine used EF Core with PostgreSQL only.

## Options

1. **EF Core used directly by application services** — `DbSet` and LINQ in the use cases; one
   `SaveChanges` per use case is one transaction, audit events included. EF Core translates for
   each provider and generates migrations per provider.
2. **EF Core behind repositories** — application code free of EF Core, but much pass-through code,
   and transactions spanning repositories need a unit-of-work abstraction on top.
3. **Dapper with hand-written SQL** — full control, but every query and migration written twice,
   once per dialect.

## Decision

Option 1.

- `AutoCanvas.Persistence` holds the provider-neutral model: an abstract `AutoCanvasDbContext`,
  entities and their configuration. It references EF Core only, never a provider.
- `AutoCanvas.Persistence.Postgres` and `AutoCanvas.Persistence.Sqlite` each derive a context,
  add their conventions (SQLite stores `DateTimeOffset` as a number so it can be compared and
  ordered in SQL), and own their migrations. `build/add-migration.sh` generates a migration for
  both at once; a test fails when either provider's model has changes without a migration.
- Tables and columns use snake_case through `EFCore.NamingConventions` (Apache-2.0), so the schema
  reads naturally in `psql` and `sqlite3`.
- Npgsql is licensed under the **PostgreSQL License**, a permissive license equivalent to MIT/BSD.
  It is added to the license allow list with this ADR.
- Generated migrations are exempt from analyzers, XML-doc requirements and coverage; they are never
  edited by hand once merged.

## Consequences

- Use cases stay short, and tests run them against both real databases instead of mocks.
- EF Core's translation limits apply: a query that only one provider can translate must become a
  port with an implementation per provider.
- Every schema change produces two migrations, reviewed together in one pull request.
