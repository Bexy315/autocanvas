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
| `AutoCanvas.Server` | HTTP API, authentication, CLI subcommands; scheduling and job queue from S2 | host, configuration, health, problem details, OpenAPI ([API conventions](api.md)), database selection, `migrate` command |
| `AutoCanvas.Server.Application` | Use cases: identity, folders and permissions, automations, variables and secrets, audit | audit log, database maintenance |
| `AutoCanvas.Persistence` | EF Core model shared by all databases; one project per provider (PostgreSQL, SQLite) with its migrations ([ADR-0010](../adr/0010-database-providers.md), [ADR-0014](../adr/0014-data-access-with-ef-core.md)) | model with the audit log, both providers, migrations |
| `AutoCanvas.Runner` | Reference runner implementing the runner protocol | planned (S2) |
| Web UI | Canvas editor, runs, logs, reports, administration | planned (S3) |
| `AutoCanvas.Forge` | Git and forge integrations | planned (S5) |

The dependency direction is strict: Core and Reports depend on nothing in AutoCanvas; the server
and runner depend on them, never the reverse.

## Runtime: a pipeline run

*Planned in the server; the decisions exist in Core.* A trigger (manual, schedule, webhook)
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
