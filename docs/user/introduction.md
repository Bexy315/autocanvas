---
slug: /
sidebar_position: 1
---

# Introduction

AutoCanvas is a self-hosted automation and CI/CD engine for developers and technicians who need to
automate things — builds, tests, deployments, or any scripted chore.

- **Pipelines as code or on a canvas.** Keep pipeline definitions as YAML in your repository, or
  build them visually in the web UI. Both describe the same model.
- **Lightweight runners.** Jobs run on runners you place where the work has to happen; the runner
  protocol is an open, versioned specification so runners can be written in any language.
- **Test reports built in.** Publish JUnit, TRX or Cobertura files from a job and get test results,
  history and coverage per run without plugins.
- **Simple to operate.** One server, a PostgreSQL database and as many runners as you need, with
  local users, folders and straightforward sharing.

:::caution Pre-alpha

AutoCanvas is in its first milestone. There is nothing to install yet; this documentation grows
with the code. The [technical documentation](../technical/architecture.md) and the
[decision records](../adr/README.md) describe where the project is heading.

:::

AutoCanvas is open source under the MIT License.
