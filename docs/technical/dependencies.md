# Dependencies

AutoCanvas is MIT-licensed, so every dependency must use a license that allows redistribution under
MIT. CI enforces this against the allow list in
[`build/allowed-licenses.json`](../../build/allowed-licenses.json): with `nuget-license` for NuGet
packages and with `npm run licenses` for the web UI's npm packages
([ADR-0019](../adr/0019-npm-licenses.md)).

## Allowed licenses

| License | Why it is compatible |
|---|---|
| MIT | Same license |
| Apache-2.0 | Permissive; keep NOTICE files when redistributing binaries |
| BSD-2-Clause, BSD-3-Clause | Permissive |
| MS-PL | Permissive, used by some older .NET packages |
| PostgreSQL | Permissive (MIT/BSD-like); Npgsql, the PostgreSQL driver ([ADR-0014](../adr/0014-data-access-with-ef-core.md)) |
| ISC, 0BSD, MIT-0 | Permissive, MIT-like; common on small npm packages |
| BlueOak-1.0.0 | Permissive, with a patent grant; some npm packages (`tar`, `path-scurry`) |
| CC0-1.0 | Public domain dedication; npm data packages |
| Python-2.0 | Permissive; `argparse`, a dependency of the API type generator |
| CC-BY-3.0 | Attribution only; the SPDX license data the npm license checker reads |

### Exceptions

A package whose license is not on the list can be allowed by name, with a reason, in
`src/web/license-exceptions.json` ([ADR-0019](../adr/0019-npm-licenses.md)):

| Package | License | Why it is allowed |
|---|---|---|
| lightningcss (and its platform builds) | MPL-2.0 | The CSS compiler Vite and Tailwind run at build time; used unchanged and not part of the built UI |

Anything else (copyleft such as GPL/LGPL/AGPL, "source available", or custom licenses) needs an ADR
before it is added. Licenses can change between versions: JsonSchema.Net moved to a proprietary
EULA in version 9, so check the exact version, not only the package.

## Rules

- Versions are pinned centrally in `Directory.Packages.props`; projects never set a version.
- Restores use lock files (`packages.lock.json`, committed) and run in locked mode in CI.
- Packages come from nuget.org only (`nuget.config` clears machine-wide feeds).
- Dependabot opens grouped weekly updates for NuGet packages, GitHub Actions and both npm
  projects; npm updates wait until a version has been out for 14 days.

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

The web UI (`src/web`, [ADR-0018](../adr/0018-web-ui.md)) pins its npm packages exactly in
`package.json` and `package-lock.json`, taken only once they have been published for at least two
weeks. Runtime packages end up in the built UI; the others are build and test tools.

| Package | License | Used for |
|---|---|---|
| react, react-dom | MIT | The UI library |
| react-router | MIT | Pages and navigation |
| @tanstack/react-query | MIT | Loading and caching API data |
| openapi-fetch | MIT | The typed API client |
| @radix-ui/react-dropdown-menu, @radix-ui/react-dialog | MIT | Accessible menus and dialogs |
| lucide-react | ISC | Icons |
| clsx | MIT | Joining class names |
| tailwindcss, @tailwindcss/vite | MIT | Styling with design tokens (build time) |
| vite, @vitejs/plugin-react | MIT | Development server and build |
| typescript | Apache-2.0 | Type checking (5.9: the API type generator does not support 7 yet) |
| openapi-typescript | MIT | Generating the API types from `openapi/v1.json` |
| oxlint | MIT | Linting |
| prettier | MIT | Formatting |
| vitest, @vitest/coverage-v8, jsdom | MIT | Unit and component tests with the 75 % gate |
| @testing-library/react, /dom, /user-event, /jest-dom | MIT | Testing components the way people use them |
| @playwright/test | Apache-2.0 | Browser tests against the real server |
| license-checker-rseidelsohn | BSD-3-Clause | The npm license check |
| @types/react, @types/react-dom, @types/node | MIT | Type definitions |

Local tools (`dotnet-tools.json`): ReportGenerator (Apache-2.0) for coverage summaries,
nuget-license (Apache-2.0) for the license check, dotnet-ef (MIT) for generating migrations.
