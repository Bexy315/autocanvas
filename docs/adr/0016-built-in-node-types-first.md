# 0016. Ship a few built-in node types; decide on plugins after 0.1

- Status: Proposed
- Date: 2026-10-31

## Context

Steps are either scripts (`run:`) or nodes (`type:` with `with:` inputs,
[ADR-0007](0007-automation-format-v1.md)). Runners announce the node types they can execute
(`Capabilities.node_types` in the runner protocol), and the examples in `schema/examples/` use
`docker_build` and `http_request`. S0 left open whether node types become a manifest-driven plugin
system in format v1 or later. The origin implemented a dozen node types inside its runner, several
of them tied to its own infrastructure.

## Options

1. **Plugin system now** — node manifests (image plus input schema) so anyone can add nodes; a large
   design (distribution, trust, versioning, schema) before anything runs.
2. **A few built-ins, plugins later** — the runner implements a small fixed set; plugins are
   designed once real use shows what they need.
3. **Scripts only** — refuse every `type:` step until nodes are designed; the examples would not
   run.

## Decision

Option 2. The reference runner implements **`docker_build`** (rootless BuildKit, no Docker socket
in jobs) and **`http_request`**, the two the examples use. The server knows this set: an automation
using another node type is refused when it is saved, with a diagnostic naming the known types, and
a job is offered only to runners that announce every node type it needs. A plugin system is
decided after the first release, in its own ADR.

## Consequences

- S2 stays focused on running jobs; the format does not change.
- New node types need a runner and a server release until plugins exist; that is acceptable while
  AutoCanvas is pre-1.0.
- The known set is one list in the server, so a later plugin system replaces where it comes from,
  not how it is checked.
