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
- **Tests** run against both databases (`DatabaseTest` from TestSupport, one sealed class per
  provider) and assert on what is stored, audit entries included.
