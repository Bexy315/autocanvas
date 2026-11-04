---
sidebar_position: 2
---

# Automation files

An automation describes what runs, when and where. You can keep it as a YAML file in a repository
or build it on the canvas; both use the format described here, so they behave the same.

:::note Specification

This page describes format version 1. The server reads and validates automation files
([Managed automations](managed-automations.md)); running them comes with the next milestone. The machine-readable form is the JSON Schema
`schema/automation.v1.json`, published at
<https://bexy315.github.io/autocanvas/schema/automation.v1.json>.

:::

## Where automations live

- **Repository automations** are files in `.autocanvas/automations/` at the root of a repository,
  one automation per file, named `<name>.yml` or `<name>.yaml`. The file is the source of truth;
  the canvas shows them read-only.
- **Managed automations** are created in the UI or through the API and stored by AutoCanvas in
  folders, with every saved version kept; see [Managed automations](managed-automations.md).

For editor support (completion, validation, hover help) in VS Code or any editor using the YAML
language server, put this line at the top of the file:

```yaml
# yaml-language-server: $schema=https://bexy315.github.io/autocanvas/schema/automation.v1.json
```

## A first automation

```yaml
version: 1
name: Build and test

triggers:
  manual: true
  push:
    branches: [main]

jobs:
  test:
    runs_on: [linux, docker]
    image: mcr.microsoft.com/dotnet/sdk:10.0
    steps:
      - run: dotnet test -c Release --report-trx
    reports:
      - format: trx
        path: '**/TestResults/*.trx'
```

A run starts on a push to `main` or by hand. The `test` job goes to a runner with the labels
`linux` and `docker`, runs in the .NET SDK image with the repository already checked out, runs the
tests and publishes the results, which then appear on the run.

## Top level

| Key | Required | Meaning |
|---|---|---|
| `version` | yes | Always `1` for this format. |
| `name` | | Display name; defaults to the file name. |
| `description` | | Shown in the UI. |
| `triggers` | | What starts a run (below). |
| `variables` | | Values readable as `${{ vars.NAME }}`; they override folder variables of the same name. |
| `timeout_minutes` | | Default time limit per job; without it, 60 minutes. |
| `concurrency` | | `parallel` (default) or `single`: a new run starts only when no other run of this automation is active. |
| `changed_when` | | Expression telling whether a successful run changed anything; runs that did not are collapsed in the history. Useful for frequent checks. |
| `jobs` | yes | The jobs, keyed by job id. |
| `x-…` | | Extension data AutoCanvas keeps but does not interpret, such as the canvas layout in `x-editor`. |

Ids of jobs, steps, services, outputs and cases use lower-case letters, digits, `_` and `-`, and
start with a letter.

## Triggers

```yaml
triggers:
  manual: true                    # start by hand (UI or API)
  push:                           # repository automations only
    branches: [main, 'release/*']
    tags: ['v*']
    paths: ['src/**']             # only when a changed file matches
  schedule:
    - cron: '0 3 * * 1-5'         # five-field cron, UTC unless timezone is set
      timezone: Europe/Berlin
    - interval: 5m                # s, m, h or d; at least 10s
  webhook: true                   # signed HTTP call to the automation's webhook URL
  automation_completed:
    automation: build
    on: success                   # success | failure | always
```

What each trigger does, and when runs start, is described in [Triggers](triggers.md).

## Jobs

Jobs without `needs` start in parallel. A job runs its steps one after another in one workspace.

| Key | Meaning |
|---|---|
| `name` | Display name. |
| `needs` | Jobs that must finish first: `needs: [build]`, or with details `needs: [{ job: build, on: always }]`. `on` is `success` (default), `failure` or `always`. |
| `if` | Condition; the job is skipped when it is false. |
| `runs_on` | Runner labels, as a string or list; the job goes to a runner that has all of them. |
| `image` | Container image the steps run in. |
| `checkout` | Repository automations check out their own commit into the workspace by default. `checkout: false` turns that off; a list checks out specific repositories (see below). |
| `env` | Environment variables for all steps. |
| `services` | Containers next to the job, reachable by their key as host name (see below). |
| `steps` | The steps, at least one. |
| `outputs` | Values published to later jobs: `digest: ${{ steps.build.outputs.digest }}`, read as `${{ jobs.image.outputs.digest }}`. Nothing else of a job is visible to other jobs. |
| `reports` | Test and coverage files collected after the steps, even if a step failed: `format` (`junit`, `trx`, `cobertura`), `path` (glob or list of globs), optional `name`. See [Test reports](reports.md). |
| `timeout_minutes` | Time limit for this job; a job that runs longer fails (and is retried if `retry` allows). |
| `retry` | `max_attempts` (2–10, including the first) and optional `delay_seconds`. |
| `finally` | Runs once every other job is done, whatever happened; `${{ run.status }}` tells how the run went. Cannot have `needs`. |
| `on_cancel` | For a `finally` job: also run when the run is cancelled. |

### Checking out repositories

```yaml
checkout:
  - repository: self                 # this automation's repository
  - repository: https://github.com/example/tools
    ref: v2
    path: tools
    depth: 0                         # full history; default 1
```

`repository` is `self`, the name of a repository connected to AutoCanvas, or an https URL:

- `self` is the repository a repository automation comes from, at the run's commit; a managed
  automation has none, and its run fails with a message;
- a name is a repository connected in the automation's folder or a folder above it, checked out at
  its branch with its credential ([Repositories](repositories.md#runs-and-checkouts));
- an https URL is checked out without a credential.

Without `checkout`, a repository automation checks out its own commit; a managed one checks out
nothing.

### Services

```yaml
services:
  db:
    preset: postgres                 # postgres, mysql or redis
    version: 17
    env:
      POSTGRES_PASSWORD: test
  cache:
    image: valkey/valkey:8
    ready: valkey-cli ping           # retried until it succeeds before the steps start
```

A service uses either a `preset`, which brings a known image, environment and readiness check, or
an `image`. Services are removed with everything they stored when the job ends.

| Preset | Image (default `version`) | Environment | Ready when |
|---|---|---|---|
| `postgres` | `postgres:17` | `POSTGRES_PASSWORD=postgres` | `pg_isready -U postgres` succeeds |
| `mysql` | `mysql:8.4` | `MYSQL_ROOT_PASSWORD=mysql` | `mysqladmin ping` succeeds |
| `redis` | `redis:7` | — | `redis-cli ping` succeeds |

`version` replaces the image tag, `env` adds to or overrides the preset's environment, and `ready`
replaces its readiness check. A service gets two minutes to become ready.

## Steps

A step either runs a script or uses a node type.

```yaml
steps:
  - run: ./build.sh                  # script step
    shell: bash                      # bash, sh, pwsh or cmd; default bash, else sh
    working_directory: src

  - id: build                        # node step
    name: Build image
    type: docker_build
    with:
      image: my-app
      tags: [latest]
```

Common keys: `id` (to read the step's outputs), `name`, `if`, `env`, `continue_on_error`,
`timeout_minutes`.

A script step sets outputs by appending `name=value` lines to the file named by the
`AUTOCANVAS_OUTPUT` environment variable:

```sh
echo "version=1.4.2" >> "$AUTOCANVAS_OUTPUT"
```

The node types are `http_request` and `docker_build`; their inputs and outputs are in
[Node types](nodes.md). Node steps have the outputs their node type defines; `outputs` on a node step adds more, computed
from expressions. Steps' outputs are readable by later steps of the same job as
`${{ steps.ID.outputs.NAME }}`.

### When steps fail

Steps run one after another. A step whose `if` is false is skipped and the job goes on. When a step
fails or times out, the remaining steps are skipped and the job fails — unless the step has
`continue_on_error: true`, in which case the job goes on as if it had succeeded. If the runner
itself cannot execute a step (for example, the requested shell is missing), the job fails even with
`continue_on_error`, because that says nothing about the step.

Work that must happen whatever the outcome, such as reporting or cleaning up, belongs in a job with
`finally: true`.

## How a run proceeds

- Jobs without `needs` start right away; the others start when all their needs have finished and
  each need is satisfied (`on: success` by default). A job whose need is not satisfied, or whose
  `if` is false, is **skipped** — and jobs that need it with `on: success` are skipped in turn.
- A failed job with `retry` runs again after `delay_seconds`, until `max_attempts` is reached.
- `finally` jobs start once every other job has finished, whatever their outcome.
- The run **succeeds** when no job failed (skipped jobs do not count as failures), **fails** when a
  job failed — a failing `finally` job fails the run too — and is **cancelled** when someone cancels
  it. Cancelling stops all running jobs; `finally` jobs run afterwards only if they have
  `on_cancel: true`.

## Flow control on the canvas

Two job types do not run on a runner and are resolved by AutoCanvas itself:

```yaml
route:
  type: switch
  needs: [check]
  expression: ${{ jobs.check.outputs.environment }}
  cases:
    - name: production
      when: prod
    - name: staging
      when: stage
    - name: skip
      default: true

deploy-prod:
  needs: [{ job: route, case: production }]
  steps:
    - run: ./deploy.sh prod

done:
  type: join
  needs: [deploy-prod]
```

A **switch** evaluates its expression and activates the first case whose `when` equals the result
(compared as text, case-sensitively), or the `default` case; without a match and without a default,
no case is activated. Jobs that need the switch with a `case` run only when
that case was activated; the others are skipped. A **join** waits for its needs and passes through,
which keeps branches tidy on the canvas.

Expressions are described in [Expressions](expressions.md); the messages AutoCanvas shows for
mistakes in a file are listed in [Problems in automation files](diagnostics.md).
