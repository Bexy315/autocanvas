# 0002. Use "AutoCanvas" as the working name

- Status: Accepted
- Date: 2026-10-29

## Context

The project needs a name for the repository, .NET namespaces, package IDs and the documentation
site. A check on 2026-10-29 found:

- npm `autocanvas` and NuGet `AutoCanvas*` are free.
- The GitHub organization `autocanvas` is taken; about twenty small repositories named "AutoCanvas"
  exist, nearly all scripts for the Canvas learning management system.
- `autocanvas.com` is registered; `.dev`, `.io` and `.org` appeared free.
- "AutomationCanvas" was free everywhere but is longer.

## Options

1. **AutoCanvas** — short and descriptive; shares search results with Canvas-LMS scripts and
   cannot have a matching GitHub organization.
2. **AutomationCanvas** — unambiguous and fully available, but long.
3. **Find a new name now** — delays the start for a decision that can still change cheaply.

## Decision

Option 1, as a **working name**. The repository lives under the maintainer's account, so the taken
organization does not matter yet.

## Consequences

- Namespaces use `AutoCanvas.*`, npm packages `@autocanvas/*`.
- Renaming stays cheap until the first public release (0.1): after that, package IDs, image names
  and the docs URL become public contracts. Revisit the name before 0.1.
