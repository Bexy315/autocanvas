---
sidebar_position: 10.5
---

# Triggers

An automation's `triggers` say what starts its runs. Besides starting them by hand (`manual: true`),
runs start on a schedule, when another automation finishes, or — with the next release step — on a
webhook call. Push triggers come with the repository integrations (S5).

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
```

`GET /api/v1/automations/{id}/triggers` (Viewer) shows what the current version of an automation
reacts to: its schedule entries with when each fires next and last fired, and the automation it
follows.

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
