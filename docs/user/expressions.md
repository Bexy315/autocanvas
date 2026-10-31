---
sidebar_position: 3
---

# Expressions

Expressions insert values into an automation when it runs. They are written as `${{ … }}` inside
any text value — scripts, node inputs, environment variables, outputs — and as conditions (`if`,
`changed_when`).

```yaml
- run: echo "Building ${{ run.commit_short_sha }} on ${{ trigger.branch }}"
  if: trigger.branch == 'main' && vars.DEPLOY == 'true'
```

## What an expression can read

| Context | Values |
|---|---|
| `vars.NAME` | Variables of the instance, the automation's folders and its file; see [Variables and secrets](variables-and-secrets.md). Names are not case-sensitive. |
| `secrets.NAME` | Secrets of the instance, the automation's folders and the automation; see [Variables and secrets](variables-and-secrets.md). Masked in logs. Names are not case-sensitive. |
| `steps.ID.outputs.NAME` | Outputs of an earlier step of the same job, by the step's `id`. |
| `jobs.JOB.outputs.NAME` | Outputs a job published under its `outputs`. |
| `jobs.JOB.result` | `success`, `failure`, `cancelled` or `skipped`. |
| `trigger.event` | What started the run: `manual`, `push`, `schedule`, `webhook` or `automation_completed`. |
| `trigger.branch`, `trigger.ref`, `trigger.tag`, `trigger.sha`, `trigger.short_sha`, `trigger.before`, `trigger.pusher`, `trigger.message` | Details of a push. |
| `trigger.body`, `trigger.headers.NAME`, `trigger.query.NAME` | Request of a webhook trigger. |
| `run.id`, `run.automation_name`, `run.triggered_by`, `run.branch`, `run.commit_sha`, `run.commit_short_sha` | The run itself. |
| `run.status` | How the run went so far; meant for `finally` jobs. |

Output names are case-sensitive and written exactly as declared. Names in paths may contain `-`,
as in `jobs.build-image.result`.

### What is available where

Values only exist once whatever produces them has run, so not everything can be read everywhere:

| Place | Can read |
|---|---|
| `variables` | `trigger` and `run` only. |
| A job's `if`, `image`, `runs_on`, `env`, `checkout`, `services`, `reports`; a switch's `expression` | `vars`, `secrets`, `trigger`, `run`, and `jobs` that have finished before this job starts: the jobs it needs, the jobs those need, and so on. |
| A step | The same, plus `steps` before it in the same job. |
| A node step's `outputs` | The same, plus the step's own outputs. |
| A job's `outputs` | The same, plus all steps of the job. |
| A `finally` job | Every other job, since it runs after all of them. |
| `changed_when` | `vars`, `secrets`, `trigger`, `run` and every job. |

Step outputs never leave their job. To use a value in another job, publish it under the job's
`outputs` and read `jobs.JOB.outputs.NAME` from a job that needs it.

All of this is checked when the automation is saved: an expression that reads something unknown,
or something not available at its place, is reported with the reason and how to fix it (see
[Problems in automation files](diagnostics.md)). A secret never travels through job outputs: a
value equal to a secret is masked before it is stored.

## Operators

Inside `${{ … }}`, and in conditions, expressions may use:

| Syntax | Meaning |
|---|---|
| `'text'` | Text literal, single-quoted; `''` inside is a quote: `'it''s'`. |
| `42`, `-1.5`, `true`, `false`, `null` | Number, boolean and null literals. |
| `a == b`, `a != b` | Equality. Values are compared as text, case-sensitively, so `1.0 == 1` is false. |
| `a && b`, `a \|\| b`, `!a` | Logic. The right side is only evaluated when needed. |
| `( … )` | Grouping. |

Precedence from low to high: `||`, `&&`, `==`/`!=`, `!`. Comparisons cannot be chained
(`a == b == c`); combine them with `&&` or `||`.

Every value is text or `null`; `true` and `false` are the texts `true` and `false`. A value counts as
**false** when it is `false`, empty, `0` or `null`, and as true otherwise.

Inside text, an expression is replaced by its value; `null` becomes empty text.

## Conditions

A condition is a single expression. The `${{ }}` around it may be left out:
`if: trigger.branch == 'main'` and `if: ${{ trigger.branch == 'main' }}` are the same. Mixing both
forms, as in `if: ${{ a }} && ${{ b }}`, is an error — write `if: a && b`.

A switch's `expression` is text with expressions, like any other value; its result is compared to
each case's `when`.

Functions (for example to check a prefix) will be added in later versions without changing the
meaning of existing expressions.
