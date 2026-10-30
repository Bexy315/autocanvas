# 0009. Parse automation files from the YAML node tree

- Status: Accepted
- Date: 2026-10-30

## Context

`AutoCanvas.Core` must turn automation files into a model and report every mistake with a precise
location, because the messages are shown to users in the UI and CLI. Core has no dependencies
without an ADR (its `AGENTS.md`); a YAML library is the expected exception.

The origin engine deserialized YAML into mutable classes with YamlDotNet's object deserializer,
ignored unknown keys, rewrote some keys with regular expressions before parsing, and extracted
`x-editor` with a hand-written regex. Typos such as `need:` passed silently, and errors carried no
position beyond YAML syntax errors.

## Options

1. **Object deserialization** (as in the origin) — little code, but unknown keys, alternative
   shapes (`needs: [a]` vs. objects, `checkout: false` vs. a list) and positions are awkward or
   impossible to handle.
2. **Validate against the JSON Schema, then deserialize** — one source of rules, but schema error
   messages are generic, positions are lost after YAML → JSON, and it adds a schema library to
   Core.
3. **Walk YamlDotNet's node tree** (`YamlStream`) and build an immutable model by hand — more code,
   but every rule produces a tailored message with line, column and document path.

## Decision

Option 3, with YamlDotNet (MIT) as Core's only dependency.

- The parser reads the node tree and checks exactly what the JSON Schema checks; a conformance
  test keeps the two in step (every example in `schema/examples/` is accepted by both, every file
  in `schema/examples/invalid/` rejected by both).
- Scalars are typed by the YAML 1.2 core schema: `on`, `yes` and `no` stay text.
- Unknown keys are errors, with "did you mean" hints and pointers from the origin's key names to
  their replacements.
- Free-form data (node inputs under `with`, `x-…` extensions) becomes `JsonElement`, immutable and
  part of the BCL.
- Limits guard against hostile input: 1 MiB of text, 100,000 nodes with aliases expanded (which
  defeats "billion laughs" documents), 64 levels of nesting.
- Diagnostic codes (`AC0001`…) are stable and documented for users.

For the conformance test, **Corvus.Json.Validator** (Apache-2.0) validates against JSON Schema
2020-12. JsonSchema.Net was rejected: since version 9 it is published under a proprietary EULA,
which the license gate does not allow.

## Consequences

- Error messages can be as helpful as we make them; each rule is a few lines of code with a test.
- The schema and the parser are two implementations of one format. The conformance test, and the
  rule that both change in the same PR, keep them consistent.
- Semantic checks (needs naming existing jobs, cycles, expressions) build on this model in the
  validator, not in the parser.
