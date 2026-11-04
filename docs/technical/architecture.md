---
sidebar_position: 1
---

# Architecture

This page follows a light version of [arc42](https://arc42.org): context, building blocks, runtime
and deployment. It describes the target architecture and is filled in as milestones land; sections
marked *planned* do not exist in code yet.

## Context

AutoCanvas sits between the people who define automation, the places where code lives and the
machines where work runs.

| Actor / system | Interaction |
|---|---|
| Users | Define pipelines (YAML in a repository or on the canvas), trigger runs, read logs and test reports, share folders |
| Git hosting (generic Git, later GitHub, GitLab, Gitea) | Source of in-repository pipelines and code; sends webhooks; receives commit statuses |
| Runners | Pull jobs from the server, execute steps, stream logs, upload reports and artifacts |
| Notification targets | Receive run results (webhooks, chat, e-mail) — *planned* |

## Building blocks

| Block | Responsibility | Status |
|---|---|---|
| `AutoCanvas.Core` | Automation model, parsing, validation, expressions, orchestration decisions — pure code | model, parser, validator, expressions, orchestration decisions |
| `AutoCanvas.Reports` | Reading test and coverage reports into a normalized model | test results (TRX, JUnit), coverage (Cobertura) |
| `AutoCanvas.Server` | HTTP API, authentication, CLI subcommands, runner endpoint; scheduling from S2 | host, configuration, health, problem details, OpenAPI ([API conventions](api.md)), authentication, database selection and master key, `migrate` and `admin` commands, audit retention, runs API, job queue sweeper, runner gRPC services on their own port ([ADR-0017](../adr/0017-runner-endpoint-and-credentials.md)) |
| `AutoCanvas.Server.Application` | Use cases: identity, folders and permissions, automations, variables and secrets, audit | users, groups, sessions and tokens; folders and permissions; managed automations; variables, secrets and the master key; audit log and its queries |
| `AutoCanvas.Persistence` | EF Core model shared by all databases; one project per provider (PostgreSQL, SQLite) with its migrations ([ADR-0010](../adr/0010-database-providers.md), [ADR-0014](../adr/0014-data-access-with-ef-core.md)) | complete S1 model, both providers, migrations |
| `AutoCanvas.Protocol` | C# generated from the runner protocol in `proto/`: messages, server and client stubs | runner protocol v1 |
| `AutoCanvas.Forge` | Forges repositories live on, behind capabilities ([ADR-0021](../adr/0021-repositories-per-folder.md)) | Generic Git through the `git` command line; GitHub through a GitHub App: its REST API and JWTs, git with installation tokens ([ADR-0022](../adr/0022-github-app.md)) |
| `AutoCanvas.Runner` | Reference runner implementing the runner protocol | jobs in Docker containers with services, checkouts, scripts and the built-in node types ([ADR-0016](../adr/0016-built-in-node-types-first.md)) |
| Web UI (`src/web`) | Canvas editor, runs, logs, reports, administration | React SPA served by the server ([Web UI](web-ui.md), [ADR-0018](../adr/0018-web-ui.md)) |

The dependency direction is strict: Core and Reports depend on nothing in AutoCanvas; the server
and runner depend on them, never the reverse.

## Runtime: a pipeline run

A trigger (manual, schedule, webhook)
creates a run; Core decides which jobs are ready (`RunPlanner`);
the server enqueues them in its own job queue ([ADR-0010](../adr/0010-database-providers.md));
a runner with matching labels leases a job over the [runner protocol](../protocol/runner-v1.md),
receives its steps one at a time, fully resolved ([ADR-0008](../adr/0008-server-resolves-steps.md)), streams logs and reports back, and
completes it; Core decides what runs next (`StepPlanner` within a job, `RunPlanner` between jobs)
until the run finishes (`RunOutcome`). Core's decisions are pure functions of the run state: the
server stores the state, carries out the returned actions and asks again when something changes.

## Deployment

*Planned.* One server container with SQLite, or with PostgreSQL for production and for several
server instances ([ADR-0010](../adr/0010-database-providers.md)), started with `docker compose`,
plus any number of runners registered with a one-time token.

## Quality

How code quality is enforced is described in [ADR-0006](../adr/0006-quality-gates.md); dependency
rules are in [Dependencies](dependencies.md); how accounts, permissions and secrets are protected is
in the [Security model](security.md).
