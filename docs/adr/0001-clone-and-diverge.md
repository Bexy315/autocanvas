# 0001. Clone the origin engine once, then develop independently

- Status: Accepted
- Date: 2026-10-29

## Context

AutoCanvas grows out of an automation engine, runner and test-report parser that the maintainer
built for a private homelab platform. That engine works well, but it is woven into the platform: its
identity provider and object-level grants, platform service clients (notifications, package
registry, directory), and platform-specific step types.

AutoCanvas has different goals: standalone operation, a stricter development process and quality
bar, performance as a first-class concern, and possibly runners in other languages.

## Options

1. **Shared core** — extract a library both products use. Keeps one engine, but every change must
   fit both products and their release cycles; the stricter process would leak into, or be held
   back by, the platform.
2. **Upstream with plugins** — AutoCanvas becomes the engine and the platform runs it with plugins.
   Clean long-term, but forces extension points designed around the platform from day one.
3. **Clone and diverge** — take a one-time copy, strip everything platform-specific, and develop
   independently.

## Decision

Option 3. After the initial migration AutoCanvas has no code, build or release dependency on the
origin. Features may be carried over by hand in either direction, always adapted to the target
(see the `harvest` skill). The origin's git history is not imported, because it contains
environment-specific configuration.

## Consequences

- AutoCanvas is free to redesign anything: data model, protocol, auth, UI.
- No compatibility work and no plugin seam for the origin platform.
- Fixes found in one product must be ported manually to the other if wanted.
- Whether the origin platform later adopts AutoCanvas is a separate decision, made only if the
  project takes off.
