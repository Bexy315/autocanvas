---
sidebar_position: 4
---

# Problems in automation files

When an automation file has a mistake, AutoCanvas reports it with a code, the place in the file and
a message that says how to fix it:

```text
(6:16) AC0104 jobs.build.steps[0].shell: 'zsh' is not allowed here. Use one of: bash, sh, pwsh, cmd.
```

- `(6:16)` is line and column in the file.
- `AC0104` is the kind of problem; the codes below never change meaning.
- `jobs.build.steps[0].shell` is the path in the document: the `shell` of the first step of job
  `build`.

All problems in a file are reported at once, not only the first.

In the editor, problems are listed under **Problems** while you type, marked at their line in the
file and on the card of the job they are in; choosing one jumps to its place.

![A problem listed below the editor, marked in the file and on its card](img/diagnostics-problems-light.png#gh-light-mode-only)
![A problem listed below the editor, marked in the file and on its card](img/diagnostics-problems-dark.png#gh-dark-mode-only)

## Reading the file

| Code | Meaning | What to do |
|---|---|---|
| `AC0001` | The file is not valid YAML. | Fix the syntax at the given position: indentation, unclosed brackets or quotes, a key that appears twice. |
| `AC0002` | The file is too large: more than 1 MiB, more than 100,000 values once anchors and aliases are expanded, or nested deeper than 64 levels. | Split the automation, or avoid deeply nested aliases. |
| `AC0003` | The file is empty or contains several YAML documents (`---`). | Keep one automation per file. |

## Structure

| Code | Meaning | What to do |
|---|---|---|
| `AC0101` | A value has the wrong type, for example a list where text is expected, or a number where text is expected. | Change the value; put text that looks like a number or `true` in quotes. |
| `AC0102` | A required key is missing, such as `version`, `jobs` or a step's `run`. | Add the key named in the message. |
| `AC0103` | A key is not known at this place, often a typo. | Use the key suggested in the message, or remove it. Keys starting with `x-` are allowed at the top level for your own data. |
| `AC0104` | A value is not allowed: outside a range, not one of the listed choices, or not in the required form (for example an id with capital letters). | Use a value as described in the message. |
| `AC0105` | Keys that exclude each other are used together, or none of several alternatives is given — for example a step with both `run` and `type`. | Keep exactly one of the alternatives. |
| `AC0106` | The `version` is not one this AutoCanvas can read. | Use `version: 1`. |

## How the parts fit together

| Code | Meaning | What to do |
|---|---|---|
| `AC0201` | A job needs a job that does not exist. | Fix the job id; the message suggests a close match. |
| `AC0202` | A job lists the same job twice in `needs`. | Keep one entry. |
| `AC0203` | Jobs need each other in a cycle, so none of them can start. The message lists the cycle. | Remove one of the needs in the cycle. |
| `AC0204` | A job needs a `finally` job, which runs after every other job. | Remove the need, or make the needed job a normal job. |
| `AC0205` | A need names a `case` of a job that is not a switch, or a case the switch does not have. | Use a case name listed in the message. |
| `AC0206` | A switch has two cases with the same name, or more than one `default` case. | Rename the case, or keep one default. |
| `AC0207` | Two steps of a job have the same `id`. | Give each step its own id. |

## Expressions

| Code | Meaning | What to do |
|---|---|---|
| `AC0301` | An expression is not valid syntax, for example an unclosed `${{`, a missing value after `==`, or a condition that mixes `${{ }}` with plain text. The message gives the character position. | Fix the expression; see [Expressions](expressions.md). |
| `AC0302` | An expression reads something that does not exist (`run.pipeline_name`, an output a job does not publish), or something that cannot be read at this place (`steps` outside a job's steps, `vars` in variables). | Use one of the values listed in the message. |
| `AC0303` | An expression reads a job or step whose values are not available yet: a job that is not among this job's needs, or a step that comes later. | Add the job to `needs`, or move the step. |

Values are read by YAML 1.2 rules: `on`, `yes` and `no` are text, only `true` and `false` are
booleans.

## See also

- [Automation files](automations.md): the format these problems are about.
- [Expressions](expressions.md): what expressions may read where.
