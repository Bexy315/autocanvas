---
sidebar_position: 2
---

# Automation files

An automation describes what runs, when and where. You can keep it as a YAML file in a repository
or build it on the canvas; both use the format described here, so they behave the same.

:::note Specification

This page describes format version 1. Parsing and running automations are being built; until then
the format is a specification. The machine-readable form is the JSON Schema
`schema/automation.v1.json`, published at
<https://bexy315.github.io/autocanvas/schema/automation.v1.json>.

:::

## Where automations live

- **Repository automations** are files in `.autocanvas/automations/` at the root of a repository,
  one automation per file, named `<name>.yml` or `<name>.yaml`. The file is the source of truth;
  the canvas shows them read-only.
- **Managed automations** are created in the UI and stored by AutoCanvas.

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
| `timeout_minutes` | | Default time limit per job. |
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
| `reports` | Test and coverage files collected after the steps, even if a step failed: `format` (`junit`, `trx`, `cobertura`), `path` (glob or list of globs), optional `name`. |
| `timeout_minutes` | Time limit for this job. |
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

`repository` is `self`, the name of a repository connected to AutoCanvas, or an https URL.

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

Node steps have the outputs their node type defines; `outputs` on a node step adds more, computed
from expressions. Steps' outputs are readable by later steps of the same job as
`${{ steps.ID.outputs.NAME }}`.

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
(compared as text), or the `default` case. Jobs that need the switch with a `case` run only when
that case was activated; the others are skipped. A **join** waits for its needs and passes through,
which keeps branches tidy on the canvas.

Expressions are described in [Expressions](expressions.md); the messages AutoCanvas shows for
mistakes in a file are listed in [Problems in automation files](diagnostics.md).
