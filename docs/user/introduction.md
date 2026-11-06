# Introduction

AutoCanvas is a self-hosted automation and CI/CD engine for developers and technicians who need to
automate things: builds, tests, deployments, or any scripted chore. You run it yourself, on one
machine or several, and it keeps your pipelines, their runs and their results.

:::caution Pre-alpha, and how it came about

There is no release yet; what is documented here works, but may still change. AutoCanvas is the
standalone version of the automation engine in the maintainer's homelab, which grew out of
experiments, and it has been written almost entirely by AI coding agents. Read and test it with
that in mind before you rely on it.

:::

## What it does

- **Pipelines as code or on a canvas.** An automation is a YAML file. Keep it in your Git
  repository, or build and edit it visually in the web UI; both are the same file.
- **Runners where the work is.** Jobs run on runners you place where the work has to happen, in
  containers with services beside them. The runner protocol is open and versioned.
- **Test reports built in.** Jobs publish JUnit, TRX or Cobertura files and get test results,
  history, flaky tests and coverage per run, without plugins.
- **Repositories and GitHub.** Pushes and pull requests start runs; results go back as commit
  statuses and a comment on the pull request.
- **Simple to operate.** One server with its web UI, SQLite or PostgreSQL, and as many runners as
  you need. Local users, folders and roles, an audit log.

![The editor: canvas and file side by side](img/tour-editor-light.png#gh-light-mode-only)
![The editor: canvas and file side by side](img/tour-editor-dark.png#gh-dark-mode-only)

## How it fits together

- The **server** holds everything: users, folders, automations, runs, logs and reports. It serves
  the web UI and the HTTP API, and hands out jobs.
- **Folders** organize automations and decide who may do what: viewers look, operators start runs,
  editors change automations, owners share. Folders hold variables and secrets for everything in
  them.
- An **automation** is one YAML file with triggers (by hand, on a schedule, by webhook, on a push or
  pull request, after another automation) and jobs made of steps.
- A **run** is one execution of an automation. Its jobs go to **runners** with the right labels;
  runners run them in containers and send back logs and reports.

## Where to go next

| You want to | Read |
|---|---|
| Install it | [Installation](installation.md) |
| Learn it by doing | [Your first automation](web-ui.md) |
| Write automation files | [Automation files](automations.md), [Triggers](triggers.md), [Expressions](expressions.md) |
| Run it for a team | [Operations](../technical/operations.md), [Security](../technical/security.md) |
| Understand or extend it | [Architecture](../technical/architecture.md), [API conventions](../technical/api.md), [Runner protocol](../protocol/runner-v1.md) |

AutoCanvas is open source under the MIT License.
