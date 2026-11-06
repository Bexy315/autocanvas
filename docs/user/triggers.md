---
sidebar_position: 10.5
---

# Triggers

An automation's `triggers` say what starts its runs. Besides starting them by hand (`manual: true`),
runs start on a schedule, when another automation finishes, on a webhook call, or — for automations
from a repository — on a push ([Repositories](repositories.md#pushes)) or a pull request
([below](#pull-requests)).

```yaml
triggers:
  manual: true
  schedule:
    - cron: '0 3 * * 1-5'      # 03:00 on weekdays
      timezone: Europe/Berlin
    - interval: 15m
  automation_completed:
    automation: build
    on: success
  webhook: true
```

`GET /api/v1/automations/{id}/triggers` (Viewer) shows what the current version of an automation
reacts to: its schedule entries with when each fires next and last fired, and the automation it
follows.

## In the web UI

An automation's **Triggers** tab shows what the current version reacts to:
- **By hand**: whether it starts by hand.
- **Webhook**: its webhook and the address.
- **Schedules**: each schedule with when it fires next and when it last fired.
- **After another automation**: the automation it follows, and on which result.

Triggers are changed in the file (**Change in the editor**). The webhook's address is managed in the
tab:

- **Create address**: anyone with the Operator role creates it, by default for signed calls only.
  The dialog that follows shows the address (from the public address set under
  **Administration** → **Instance**, else from the address in the browser) and the signing secret
  **once**, with copy buttons and
  an example call in **curl** and **PowerShell** that signs the body. It closes only once you
  confirm you saved them.
- **Rotate**: makes a new address; the old one stops working at once.
- **Delete**: removes the address, so calls of it fail from then on.

![A new webhook address](img/tour-webhook-light.png#gh-light-mode-only)
![A new webhook address](img/tour-webhook-dark.png#gh-dark-mode-only)

Calls start runs only while the file has `webhook: true`; the tab says so when it does not.

## Schedules

- **`cron`** takes five fields — minute, hour, day of month, month, day of week — as in crontab:
  `*/10 * * * *` every ten minutes, `0 3 * * 1-5` at 03:00 on weekdays. Times are UTC unless
  `timezone` names an IANA time zone such as `Europe/Berlin`; then daylight saving time is taken
  into account.
- **`interval`** repeats after a fixed time: `30s`, `15m`, `2h` or `1d`, at least ten seconds. The
  first run comes one interval after the automation was saved.

An expression that never fires (such as `0 0 30 2 *`) or an unknown time zone is refused when the
automation is saved. Saving it again with the same entries keeps their times; a changed or new
entry starts counting from the save.

Times missed while no server ran are not made up for: after a restart, an entry runs at its next
time. A run of an automation with `concurrency: single` does not start while another one is active;
the schedule goes on to its next time. A run started by a schedule reads `trigger.event` as
`schedule`, `trigger.schedule` as the entry (`0 3 * * 1-5`, `every 15m`) and `trigger.scheduled_at`
as the time it was due.

## When another automation finishes

`automation_completed` starts a run whenever a run of another automation finishes:

| Key | Meaning |
|---|---|
| `automation` | The other automation: its name for one in the same folder, or a path of folder names from the top, such as `team/backend/build` |
| `on` | `success` (default), `failure`, or `always` — which also counts cancelled runs |

The other automation must exist, and whoever saves the file must be allowed to see it (Viewer);
otherwise saving fails. AutoCanvas remembers the folder and the name: an automation that is renamed
is no longer followed, while a new one with the old name is. An automation cannot follow itself, and
a chain of automations starting each other stops after ten.

The run reads `trigger.event` as `automation_completed`, and `trigger.automation`,
`trigger.run_id`, `trigger.run_number` and `trigger.result` (`success`, `failure` or `cancelled`)
describe the run that finished.

Schedules and followers are started by the server within about five seconds of their time; with
several servers on one PostgreSQL database, exactly one of them starts each run.

## Pull requests

`pull_request` starts runs when a pull request into the automation's repository is opened, gets new
commits or is reopened. It works for repositories connected through the [GitHub App](github.md);
other forges follow later.

```yaml
triggers:
  pull_request:
    branches: [main, 'release/*']   # the branch it goes into; left out: every branch
    paths: ['src/**']               # only when a changed file matches; left out: any change
    types: [opened, synchronize, reopened, ready_for_review]
```

- **`types`**: `opened`, `synchronize` (new commits), `reopened`, `ready_for_review` (a draft that
  is ready) and `edited` (title or description). Left out, it is the first three.
- **Patterns** work as for pushes: `*`, `**` and `?` ([Repositories](repositories.md#pushes)).
- **Which file runs**: the automation's file at the head of the pull request, at that commit, so a
  pull request can change its own automation. As for pushes, only automations the folder has (files
  on the repository's branch) run.
- **Forks**: a run of a pull request from a fork gets **no secrets** (`secrets.*` read as empty)
  and checks out no other repositories: its code comes from outside the folder's people. It still
  checks out the pull request's head, with a token that may only read the repository.
- **What the run reads**: `trigger.event` is `pull_request`; `trigger.number`, `trigger.title`,
  `trigger.action`, `trigger.head_branch`, `trigger.base_branch`, `trigger.sha`,
  `trigger.short_sha`, `trigger.author`, `trigger.fork` (`true` or `false`), `trigger.url` and
  `trigger.ref` (`refs/pull/12/head`). The run is started by the pull request's author.

## Webhooks

With `webhook: true`, other systems start runs by calling the automation's webhook address — a
chat bot, a monitoring alert, a script. The address is a secret URL; Operators of the folder create
it:

```sh
curl -X POST https://autocanvas.example.org/api/v1/automations/$AUTOMATION/webhook \
  -H "Authorization: Bearer $AUTOCANVAS_TOKEN" -H "Content-Type: application/json" \
  -d '{"requireSignature": true}'
```

The answer holds the `path` (`/api/v1/hooks/ach_…`) and, with `requireSignature`, the
`signingSecret`. Both are shown only this once; AutoCanvas keeps the token as a hash and the
secret encrypted with the master key. Creating an address again replaces the old one, and
`DELETE …/webhook` removes it; `GET …/webhook` shows whether there is one, without the token.

A call needs no sign-in — the token in the path is the credential:

```sh
body='{"ref": "main", "reason": "alert 42"}'
signature="sha256=$(printf '%s' "$body" | openssl dgst -sha256 -hmac "$SIGNING_SECRET" -hex | cut -d' ' -f2)"
curl -X POST "https://autocanvas.example.org/api/v1/hooks/$TOKEN" \
  -H "Content-Type: application/json" -H "X-AutoCanvas-Signature: $signature" -d "$body"
```

- **Signature**: an address created with `requireSignature` takes only calls whose
  `X-AutoCanvas-Signature` is `sha256=` and the hex HMAC-SHA256 of the exact body, keyed with the
  signing secret (`401` otherwise).
- **What the run reads**: `trigger.body` is the body — with a JSON content type it can be read
  into by property names (`trigger.body.ref`); `trigger.headers.NAME` and `trigger.query.NAME` are
  the request's headers and query. `Authorization`, `Cookie` and the signature are left out.
- **Answers**: `202` with the run's id and number; `404` when the address is unknown or the current
  version has no `webhook: true` (deliberately the same); `409` while a `concurrency: single`
  automation is running; `413` for a body over 1 MiB; `429` beyond `Webhooks:PerMinute` calls a
  minute to one address (30 by default).

## See also

- **Next**: [Expressions](expressions.md): reading what started a run.
- [Repositories](repositories.md): pushes and pull requests.
- [Runs](runs.md): following and cancelling what a trigger started.
