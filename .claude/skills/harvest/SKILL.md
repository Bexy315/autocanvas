---
name: harvest
description: Move code from the origin project (the maintainer's private automation engine, of which AutoCanvas is a clone) into AutoCanvas — what to take, what to strip, how to adapt it to AutoCanvas's specs and gates. Use for the harvest steps of the S0 plan and whenever porting a feature from the origin.
---

# Harvesting code from the origin

AutoCanvas started as a clone of an automation engine inside the maintainer's private homelab
platform and is developed independently ([ADR-0001](../../../docs/adr/0001-clone-and-diverge.md)).
Code may still be carried over, but only by deliberately adapting it — never by copying a project
wholesale.

## Where the origin is

The origin is not public. The maintainer has it checked out locally (by default as a sibling
folder, `../homelab-core`): the engine in `services/automation`, the runner in `services/runner`,
the canvas UI in `services/devops-web`. If it is not there, ask for the path. Never add the origin
as a git remote, submodule or package source.

## Never carry over

- configuration files (`appsettings*.json`, `.env`, compose files), hostnames, IPs, domains, user
  names, tokens, keys — not even as examples
- the origin's auth and authorization (object grants, identity provider integration, audit
  client), service clients for other platform services (notifications, registry feeds, directory),
  AI agent steps, Semgrep/Trivy scan steps
- generated code, migrations and lock files — regenerate them here
- the origin's git history

## Steps

1. **Pick a unit** small enough for one PR: a parser, a validator, a set of related node types.
2. **Read the origin code and its tests first.** Note what it does, its dependencies, and which
   parts are origin-specific.
3. **Check it against AutoCanvas's specs** (pipeline schema, runner protocol, ADRs). Where they
   differ, the spec wins — adapt the code, do not bend the spec to fit old code. If the spec seems
   wrong, stop and raise it.
4. **Bring it over by rewriting into place**, not by copying files verbatim:
   - AutoCanvas namespaces, naming and project boundaries (pure logic → `AutoCanvas.Core`)
   - separate pure decisions from effects; effects stay out of Core
   - satisfy the analyzers properly (no blanket suppressions), add XML docs to public API
5. **Tests**: port the origin's tests where they exist, then add what is missing until behavior is
   covered — table-driven cases for parsers and validators, including malformed input.
6. **Scan before committing**: search the diff for the never-carry-over list above
   (`git diff --cached | grep -iE 'homelab|authentik|192\.168|\.local\b|password|token'`)
   and review every hit.
7. **Document**: user docs for anything user-visible, the plan step updated, and in the PR
   description a short "Harvested from" section naming the origin files and what changed.

## Judgement calls

If the origin code is poor in a way that matters (unclear structure, missing edge cases,
performance traps), rewrite it here rather than carrying the debt over — the origin's behavior and
tests are the specification, not its code.
