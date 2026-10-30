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

Values are read by YAML 1.2 rules: `on`, `yes` and `no` are text, only `true` and `false` are
booleans.
