# Architecture decisions

Architecture decision records (ADRs) explain why AutoCanvas is built the way it is. Each one is
written once; a changed decision gets a new ADR that supersedes the old one.

| ADR | Decision | Status |
|---|---|---|
| [0001](0001-clone-and-diverge.md) | Clone the origin engine once, then develop independently | Accepted |
| [0002](0002-working-name.md) | Use "AutoCanvas" as the working name | Accepted |
| [0003](0003-pipeline-sources.md) | Pipelines live in repositories or are managed, one source each | Accepted |
| [0004](0004-runner-protocol-grpc-spec-first.md) | Runner protocol: gRPC, specification first, versioned | Accepted |
| [0005](0005-own-postgres-job-queue.md) | Dispatch jobs through an own PostgreSQL queue | Superseded by [0010](0010-database-providers.md) |
| [0006](0006-quality-gates.md) | Enforce quality gates in CI from the first commit | Accepted |
| [0007](0007-automation-format-v1.md) | Automation format version 1 | Accepted |
| [0008](0008-server-resolves-steps.md) | The server resolves steps, runners execute them | Accepted |
| [0009](0009-parsing-automation-files.md) | Parse automation files from the YAML node tree | Accepted |
| [0010](0010-database-providers.md) | Support PostgreSQL and SQLite behind provider ports | Accepted |
| [0011](0011-local-identity.md) | Own local identity: users, sessions and API tokens | Accepted |
| [0012](0012-folder-permissions.md) | Permissions through roles on a folder tree | Accepted |
| [0013](0013-secrets-at-rest.md) | Encrypt secrets with AES-GCM under a master key | Accepted |
| [0014](0014-data-access-with-ef-core.md) | Data access with EF Core, one project per database provider | Accepted |
| [0015](0015-runs-queue-and-logs.md) | Store runs with a copy of their definition, queue jobs per provider, keep logs as chunks in the database | Accepted |
| [0016](0016-built-in-node-types-first.md) | Ship a few built-in node types; decide on plugins after 0.1 | Accepted |
| [0017](0017-runner-endpoint-and-credentials.md) | Serve runners on their own HTTP/2 port, with their own credentials | Accepted |
| [0018](0018-web-ui.md) | Web UI: a React SPA served by the server, with YAML as the editor's source of truth | Accepted |
| [0019](0019-npm-licenses.md) | Check npm licenses against the shared list, with named build-time exceptions | Accepted |
| [0020](0020-first-start-setup.md) | Set up a fresh instance in the browser, with a token from the log | Proposed |
