---
name: write-adr
description: Record an architecture decision for AutoCanvas as an ADR in docs/adr — numbering, template, status lifecycle. Use when a change picks between real alternatives that shape the architecture, adds a significant dependency, changes a protocol or schema, or relaxes a quality gate.
---

# Writing an ADR

ADRs record *why* the code looks the way it does. They are short, written once, and never
rewritten — a changed decision gets a new ADR that supersedes the old one.

## Does this need an ADR?

Yes, if any of these apply:

- there were at least two reasonable options and the choice will be expensive to reverse
- a new dependency shapes the architecture (framework, database, protocol, parser), or its license
  is not on the allow list
- a public contract changes: pipeline schema, runner protocol, REST API, configuration format
- a quality gate is changed or an exception to it is made

No, for local implementation choices that a reviewer can judge from the diff alone.

## Steps

1. Find the next number: the highest `docs/adr/NNNN-*.md` plus one, four digits.
2. Create `docs/adr/NNNN-short-kebab-title.md` from the template below.
3. Add it to the index table in `docs/adr/README.md` (create the index if it does not exist yet:
   a heading, one sentence, and a table of number, title, status).
4. If it supersedes an ADR, set the old one's status to `Superseded by [NNNN](NNNN-....md)` — the
   only edit ever made to an accepted ADR.
5. Commit it in the same PR as the change it explains.

## Template

```markdown
# NNNN. Title as a decision ("Use gRPC for the runner protocol")

- Status: Proposed | Accepted | Superseded by [NNNN](NNNN-title.md)
- Date: YYYY-MM-DD

## Context

What forces are at play: requirements, constraints, what exists today. Facts, not the answer.

## Options

1. **Option A** — one or two sentences, with its main upside and downside.
2. **Option B** — …

## Decision

The chosen option and the reasons that decided it, referring back to the context.

## Consequences

What becomes easier, what becomes harder, follow-up work, and what would make us revisit this.
```

## Style

- One decision per ADR. Keep it under a page; link to docs or issues for details.
- Write for a reader a year from now who has none of today's context.
- Status starts as `Proposed` in the PR and becomes `Accepted` when the maintainer approves it.
