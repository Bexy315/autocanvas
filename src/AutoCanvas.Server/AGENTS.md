# AGENTS.md — AutoCanvas.Server

The ASP.NET Core host: HTTP API, health, configuration, and (from later S1 steps) authentication,
persistence wiring and CLI subcommands. Business rules belong in application services, not here.

- **Endpoints are thin**: bind the request, call one service, map the result. No queries or rules
  inside a route handler.
- **Every `/api/` endpoint declares** a name (`WithName`), a summary (`WithSummary`), who may call
  it and the problems it can return. `ApiContractTests` enforces the first three. Who may call it:
  `RequireAuthorization()` (signed in, password changed), a policy from `AutoCanvasPolicies`, or
  `AllowAnonymous()` — there is deliberately no fallback policy. Endpoints returning `IResult` also
  declare their success type with `Produces<T>()` so the OpenAPI document shows it.
- **Use case results** become HTTP results through `ApiResults` (`Ok`, `Created`, `NoContent`,
  `Problem`); never map `UseCaseError` by hand.
- **Errors are problem details only**, with a type from `Api/ProblemTypes.cs`. A new type needs a
  section with that anchor in `docs/technical/api.md`. Never put exception messages, connection
  strings or secret values into a response — health check descriptions included.
- **The API contract is committed** in `openapi/v1.json`. After changing an endpoint, run the
  tests with `AUTOCANVAS_UPDATE_SNAPSHOTS=1`, review the diff of `openapi/v1.json` and commit it.
  Within `/api/v1` changes are additive only (`docs/technical/api.md`).
- **XML docs on request/response types are user-facing**: they become descriptions in the OpenAPI
  document.
- **Configuration only through options classes** bound to a section and validated on start
  (`IValidateOptions<T>` + `ValidateOnStart`). New settings and short environment names go into
  `docs/technical/operations.md`; aliases live only in `AutoCanvasEnvironmentSource.Aliases`.
- **Time comes from `TimeProvider`**, never `DateTime.UtcNow`; tests use `FakeTimeProvider` from
  `AutoCanvasFactory.Clock`.
- **Tests** host the server with `AutoCanvasFactory` (in memory, `Testing` environment) and assert
  on HTTP responses, not on internals.
