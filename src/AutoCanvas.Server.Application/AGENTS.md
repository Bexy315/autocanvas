# AGENTS.md — AutoCanvas.Server.Application

The server's use cases: what happens when someone creates a folder, grants a role, saves an
automation. The host (`AutoCanvas.Server`) maps HTTP and the command line onto them.

- **One use case, one service method, one `SaveChanges`.** Work with `AutoCanvasDbContext`
  directly (ADR-0014); no repositories, no unit-of-work wrapper. The single `SaveChanges` is the
  transaction, so the change and its audit entry commit together.
- **Every state change is audited** through `IAuditLog.Record` before `SaveChanges`. New actions go
  into `AuditActions` and into the catalogue in `docs/technical/audit.md`. Details are a few facts
  (names, ids, old → new); **never secret values**.
- **No HTTP here.** Who acts and from where comes from `IAuditContext`; results are returned as
  values, and the host turns them into problem details.
- **Time and ids**: `TimeProvider` for every timestamp, `Guid.CreateVersion7(time)` for new ids.
  Ids made in the same millisecond share their start (the time) and sort in no particular order;
  order lists by an explicit column (`Position`, a number, a timestamp plus a tie-breaker), and in
  tests take the random **end** of an id when a unique suffix is needed.
- **Runs** follow ADR-0015: Core's planners decide (`RunPlanner`, `StepPlanner`, `RunOutcome`),
  `RunEngine` applies their decisions to the stored run, `JobDispatcher` moves attempts between the
  queue and runners. Use cases that queue or return attempts call `IJobSignal.NotifyAsync` after
  `SaveChanges`.
- **Tests** run against both databases (`DatabaseTest` from TestSupport, one sealed class per
  provider) and assert on what is stored, audit entries included. The test clock stands still, so
  entries of the same instant come back in no particular order: assert on them order-independently.
