---
sidebar_position: 3
---

# Expressions

Expressions insert values into an automation when it runs. They are written as `${{ … }}` inside
any text value — scripts, node inputs, environment variables, outputs — and in conditions (`if`,
`changed_when`, a switch's `expression`).

```yaml
- run: echo "Building ${{ run.commit_short_sha }} on ${{ trigger.branch }}"
  if: ${{ trigger.branch == 'main' && vars.DEPLOY == 'true' }}
```

## What an expression can read

| Context | Values |
|---|---|
| `vars.NAME` | Variables of the automation and its folders. Names are not case-sensitive. |
| `secrets.NAME` | Secrets of the automation and its folders. Masked in logs. Names are not case-sensitive. |
| `steps.ID.outputs.NAME` | Outputs of an earlier step of the same job. |
| `jobs.JOB.outputs.NAME` | Outputs a job published under `outputs`. Only jobs the current job (directly or indirectly) needs can be read. |
| `jobs.JOB.result` | `success`, `failure`, `cancelled` or `skipped`. |
| `trigger.event` | What started the run: `manual`, `push`, `schedule`, `webhook` or `automation_completed`. |
| `trigger.branch`, `trigger.ref`, `trigger.tag`, `trigger.sha`, `trigger.short_sha`, `trigger.before`, `trigger.pusher`, `trigger.message` | Details of a push. |
| `trigger.body`, `trigger.headers.NAME`, `trigger.query.NAME` | Request of a webhook trigger. |
| `run.id`, `run.automation_name`, `run.triggered_by`, `run.branch`, `run.commit_sha`, `run.commit_short_sha` | The run itself. |
| `run.status` | How the run went so far; meant for `finally` jobs. |

Output names are case-sensitive and written exactly as declared.

An expression that names something the automation cannot know is reported when the automation is
saved, not when it runs. A secret never travels through job outputs: a value equal to a secret is
masked before it is stored.

## Conditions

In `if`, `changed_when` and a switch's `expression`, the text between `${{` and `}}` may use:

| Syntax | Meaning |
|---|---|
| `'text'` | Text literal, single-quoted; `''` inside is a quote. |
| `42`, `true`, `false`, `null` | Number, boolean and null literals. |
| `a == b`, `a != b` | Equality. Values are compared as text, case-sensitively. |
| `a && b`, `a \|\| b`, `!a` | Logic. |
| `( … )` | Grouping. |

A value counts as false when it is `false`, empty, `0` or `null`, and as true otherwise. The `${{ }}`
around a condition may be left out: `if: trigger.branch == 'main'` is the same.

Functions (for example to check a prefix) will be added in later versions without changing the
meaning of existing expressions.
