# AGENTS.md — AutoCanvas.Persistence

The database model shared by every provider (ADR-0010, ADR-0014). `AutoCanvas.Persistence.Postgres`
and `AutoCanvas.Persistence.Sqlite` derive from it, add provider conventions and own the migrations.

- **Provider-neutral only.** No reference to a provider package here, no SQL, no provider column
  types. If something needs provider-specific SQL, define an interface for it and implement it in
  each provider project, registered by `AddAutoCanvasPostgres` / `AddAutoCanvasSqlite`. The job
  queue is the example: `Queue/IJobClaimer` and `Queue/IJobSignal`, with shared logic such as
  `JobRequirements` here and `PostgresJobClaimer` / `SqliteJobClaimer` in the providers.
- **Schema changes follow the `add-migration` skill**: model change here, then
  `build/add-migration.sh <Name>` for both providers in the same commit. Never edit a merged
  migration.
- **Entities are plain**: `required`/`init` properties, no behavior that needs services, no lazy
  loading. Business rules live in the application layer.
- **Configuration per entity** in an `IEntityTypeConfiguration<T>` next to it, registered in
  `AutoCanvasDbContext.OnModelCreating`. Tables are snake_case (convention), strings that are
  indexed or user-supplied have a maximum length, timestamps are UTC `DateTimeOffset`.
- **Tests** derive from `DatabaseTest` (TestSupport) with one sealed class per provider, so every
  persistence behavior is checked on SQLite and PostgreSQL. `AutoCanvas.Persistence.Tests` measures
  coverage for the provider projects too: code there needs tests there, even if application tests
  exercise it already. Tests that need the schema migrate in `InitializeAsync`
  (`Database.MigrateAsync`).
