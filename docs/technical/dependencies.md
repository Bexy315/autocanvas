# Dependencies

AutoCanvas is MIT-licensed, so every dependency must use a license that allows redistribution under
MIT. CI enforces this with `nuget-license` against the allow list in
[`build/allowed-licenses.json`](../../build/allowed-licenses.json).

## Allowed licenses

| License | Why it is compatible |
|---|---|
| MIT | Same license |
| Apache-2.0 | Permissive; keep NOTICE files when redistributing binaries |
| BSD-2-Clause, BSD-3-Clause | Permissive |
| MS-PL | Permissive, used by some older .NET packages |
| PostgreSQL | Permissive (MIT/BSD-like); Npgsql, the PostgreSQL driver ([ADR-0014](../adr/0014-data-access-with-ef-core.md)) |

Anything else (copyleft such as GPL/LGPL/AGPL, "source available", or custom licenses) needs an ADR
before it is added. Licenses can change between versions: JsonSchema.Net moved to a proprietary
EULA in version 9, so check the exact version, not only the package.

## Rules

- Versions are pinned centrally in `Directory.Packages.props`; projects never set a version.
- Restores use lock files (`packages.lock.json`, committed) and run in locked mode in CI.
- Packages come from nuget.org only (`nuget.config` clears machine-wide feeds).
- Dependabot opens grouped weekly updates for NuGet packages and GitHub Actions.

## Current direct dependencies

| Package | License | Used for |
|---|---|---|
| Meziantou.Analyzer | MIT | Additional analyzers, all projects |
| YamlDotNet | MIT | Reading automation files in `AutoCanvas.Core` ([ADR-0009](../adr/0009-parsing-automation-files.md)) |
| xunit.v3 | Apache-2.0 | Test framework |
| Shouldly | BSD-3-Clause | Test assertions |
| coverlet.MTP | MIT | Coverage collection and the 75 % gate |
| Corvus.Json.Validator | Apache-2.0 | Schema conformance test (tests only) |
| Testcontainers.PostgreSql | MIT | PostgreSQL in Docker for database tests (tests only) |
| Google.Protobuf | BSD-3-Clause | Messages of the runner protocol ([ADR-0004](../adr/0004-runner-protocol-grpc-spec-first.md)) |
| Grpc.Tools | Apache-2.0 | Generating C# from `proto/` at build time (not shipped) |
| Grpc.Core.Api | Apache-2.0 | Base types of the generated gRPC stubs |
| Grpc.AspNetCore.Server | Apache-2.0 | Serving the runner protocol in the server ([ADR-0017](../adr/0017-runner-endpoint-and-credentials.md)) |
| Grpc.Net.Client | Apache-2.0 | The runner protocol's client side: the reference runner and tests |
| Docker.DotNet.Enhanced | MIT | The reference runner's container engine client (the original Docker.DotNet has had no release since 2023; Testcontainers uses the same fork) |
| BenchmarkDotNet | MIT | Benchmarks in `benchmarks/` (not shipped) |
| Microsoft.AspNetCore.OpenApi | MIT | OpenAPI document of the server's HTTP API |
| Microsoft.AspNetCore.Mvc.Testing | MIT | Hosting the server in memory in API tests (tests only) |
| Microsoft.Extensions.TimeProvider.Testing | MIT | Controllable clock in tests (tests only) |
| Cronos | MIT | Cron schedules of automations: checking expressions and finding the next time, in a time zone |
| System.CommandLine | MIT | Commands of the server and runner executables (`migrate`, `register`) |
| Microsoft.EntityFrameworkCore (+ `.Sqlite`, `.Design`) | MIT | Data access and migrations ([ADR-0014](../adr/0014-data-access-with-ef-core.md)); `.Design` only for generating migrations |
| Npgsql.EntityFrameworkCore.PostgreSQL | PostgreSQL | PostgreSQL provider ([ADR-0014](../adr/0014-data-access-with-ef-core.md)) |
| EFCore.NamingConventions | Apache-2.0 | snake_case table and column names |

The documentation site (`docs-site/`, not shipped with the server) pins its npm packages exactly
in `package.json` and `package-lock.json`; new versions are taken only once they have been
published for at least two weeks. Besides Docusaurus and React (all MIT):

| Package | License | Used for |
|---|---|---|
| @scalar/docusaurus | MIT | The [API reference](pathname:///autocanvas/api) page |
| @scalar/api-reference | MIT | The viewer behind it, served from the site itself rather than a CDN; its connections to Scalar's services (toolbar, AI agent, telemetry, request proxy) are switched off |

Local tools (`dotnet-tools.json`): ReportGenerator (Apache-2.0) for coverage summaries,
nuget-license (Apache-2.0) for the license check, dotnet-ef (MIT) for generating migrations.
