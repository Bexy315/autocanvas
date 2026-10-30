# 0012. Permissions through roles on a folder tree

- Status: Proposed
- Date: 2026-10-30

## Context

Several people and teams share one installation. They need to see and change only their own
automations, delegate access without an administrator, and keep a few things (production
deployments, sensitive secrets) away from people who can see the rest. The origin engine used
object-level relations in an external authorization service (OpenFGA); AutoCanvas wants something
that runs inside the server, is easy to explain, and works on every supported database.

## Options

1. **Grants per object** — maximum flexibility, but every automation needs its own sharing, and
   answering "who can see this?" means walking many tuples.
2. **Global roles only** — simple, but no isolation between teams.
3. **Roles granted on folders, inherited down the tree** — the model of most file and CI systems;
   one place to look for each automation's access.

## Decision

Option 3.

- **Instance roles**: `Admin` (manages users, groups, instance variables and secrets, the audit
  log; sees everything) and `User`.
- **Folder roles**, each including the ones before it:
  - `Viewer` — see the folder, its automations and (from S2) runs and logs
  - `Operator` — also start and cancel runs, read variable values
  - `Editor` — also create, change and move automations and subfolders, set variables and secret
    values (secrets are write-only for everyone)
  - `Owner` — also manage grants, delete the folder, mark it restricted
- Roles are granted to **users or groups**. A user's effective role on a folder is the highest role
  from any grant to them or their groups on that folder or an ancestor.
- A **restricted** folder stops inheritance: grants above it do not apply inside it. Only grants on
  the restricted folder itself (and below), and admins, give access.
- The rule lives in one pure `PermissionEvaluator` (folder chain and grants in, effective role
  out), tested table-driven. List endpoints filter by the set of visible folders in the query.

## Consequences

- Moving an automation or folder changes who can access it; moving requires `Editor` on both source
  and target, and the audit log records it.
- No per-automation exceptions: something that needs different access goes into its own folder.
- External group sources (OIDC claims) can later fill group membership without changing the model.
