# 0021. Connect repositories per folder, behind a forge abstraction

- Status: Accepted
- Date: 2026-11-04

## Context

[ADR-0003](0003-pipeline-sources.md) decided that automations live either in AutoCanvas (managed)
or as files in `.autocanvas/automations/` of a repository, and that the server discovers the latter
on pushes and on demand. Until S4 the server knows no repositories:
- the automation format already has `triggers.push` and `checkout: self`;
- the runner can check out repositories over HTTPS with a credential.

What is open:
- Where a repository is connected, and who may do it.
- How permissions, variables and secrets apply to its automations.
- How the server reads files from a repository: a plain Git server has no API, while GitHub, GitLab
  and Gitea each have their own.
- How the server tells a forge about runs (commit status, pull request comments) and writes changes
  back (a branch and a pull request), where the forge supports it.

## Options

1. **Repositories are instance-wide, connected by admins** and mapped to folders. Central control,
   but every team needs an admin to start.
2. **Repositories belong to a folder**, connected by its editors. Repository automations appear in
   that folder next to managed ones and inherit its roles, variables and secrets.
3. **Forge-specific code throughout the server**: simple at first, but every further forge touches
   discovery, triggers and runs.
4. **A forge abstraction with capabilities**: each forge says what it can do (read files, check
   events, report a status, comment, open a pull request); the server uses what is there.

## Decision

Options 2 and 4.

- **A repository belongs to one folder.** Editors of the folder connect, sync and disconnect it.
  Its automations are listed in the folder and are read-only in AutoCanvas. Who may see, run and
  change them follows the folder's roles ([ADR-0012](0012-folder-permissions.md)). Variables and
  secrets resolve through the folder chain as for managed automations.
- **Repository automations are their own kind.** Each discovered file is an automation whose
  versions are the commits that changed it; runs keep the commit they ran. Deleting the file
  removes the automation; its runs stay, as they do for deleted managed automations.
- **`AutoCanvas.Forge`** holds the abstraction. A forge implements what it can:
  - read the files of `.autocanvas/automations/` at a commit;
  - resolve a branch to a commit;
  - verify and read an event (push, pull request);
  - report a commit status;
  - write and update a comment on a pull request;
  - create a branch with a commit and open a pull request.

  The server asks for a capability and explains in the UI when a forge lacks it.
- **Generic Git** is the first forge. It is any repository reachable over HTTPS, with an optional
  token or user name and password stored as a secret under the master key
  ([ADR-0013](0013-secrets-at-rest.md)). It reads files with the `git` command line, which the
  server image gains, through a shallow fetch into a cache directory. It learns about pushes from a
  signed webhook per repository or by polling. It has no status, comments or pull requests.
- **GitHub** is the second forge, through a GitHub App ([ADR-0022](0022-github-app.md)), with every
  capability.
- **Discovery** runs on a push to the default branch, on demand, and on a poll for repositories
  without webhooks. Pushes to other branches and pull requests read the files at their own commit
  for that run only.

## Consequences

- A team connects its own repositories without an admin; an admin can still restrict who connects
  what through folder roles.
- The server image gets `git`, and the server needs disk for a cache of shallow clones (in its data
  directory, removed when a repository is disconnected).
- GitLab, Gitea and Bitbucket can be added as forges later without changing discovery, triggers
  or runs. Meanwhile they work as Generic Git.
- Two kinds of automation share the folder views, runs and reports, but only managed ones can be
  edited in place. Repository automations are edited through a pull request where the forge
  supports it.
