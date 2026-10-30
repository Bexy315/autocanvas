---
name: add-migration
description: Change the AutoCanvas database schema — add or change entities and generate the EF Core migrations for PostgreSQL and SQLite together. Use whenever an entity, its configuration or the DbContext changes.
---

# Adding a database migration

AutoCanvas runs on PostgreSQL and SQLite (ADR-0010) with EF Core (ADR-0014). Each provider project
owns its own migrations, so every schema change produces **two** migrations, made and reviewed
together.

## Steps

1. Change the model in `src/AutoCanvas.Persistence/` only: entity classes, their
   `IEntityTypeConfiguration`, and `AutoCanvasDbContext` (register new configurations with
   `ApplyConfiguration`). Keep it provider-neutral:
   - no provider-specific column types or SQL; JSON is stored as text
   - lengths for every string that is indexed or user-supplied (`HasMaxLength`)
   - case-insensitive uniqueness through a normalized column, never a collation
   - `DateTimeOffset` values are UTC
2. Generate both migrations with a PascalCase name describing the change:
   ```sh
   build/add-migration.sh AddUsers        # or: pwsh build/add-migration.ps1 AddUsers
   ```
3. Review what each provider will run:
   ```sh
   dotnet ef migrations script <Previous> AddUsers --project src/AutoCanvas.Persistence.Postgres --startup-project src/AutoCanvas.Persistence.Postgres
   dotnet ef migrations script <Previous> AddUsers --project src/AutoCanvas.Persistence.Sqlite --startup-project src/AutoCanvas.Persistence.Sqlite
   ```
   Check data-preserving behavior: SQLite rebuilds tables for many changes; renames must be
   renames, not drop + add.
4. Run the tests. `MigrationTests` fails if either provider still has model changes without a
   migration (`HasPendingModelChanges`), and runs every migration on both databases.
5. Commit both migrations, the snapshots and the model change in one commit.

## Rules

- Never edit or delete a migration that is on `main`; add a new one instead.
- Never hand-write SQL in shared code. If one provider needs special SQL, it goes into that
  provider's project behind an interface (ADR-0010).
- To undo an unmerged migration: `dotnet ef migrations remove` in both provider projects.
