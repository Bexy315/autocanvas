---
sidebar_position: 8.5
---

# Repositories

Automations can live in a Git repository, next to the code they build
([ADR-0003](../adr/0003-pipeline-sources.md)). A repository is connected in a folder
([ADR-0021](../adr/0021-repositories-per-folder.md)): its files `.autocanvas/automations/*.yml` (or
`.yaml`) become **repository automations** of that folder, next to the managed ones.

:::note Status

Repositories are connected through the HTTP API so far; the web UI follows later in S5, as do push
and pull request triggers and GitHub.

:::

## Connecting a repository

Any Git repository reachable over `https` (or `http` on a trusted network) can be connected
("Generic Git"). Editors of a folder connect one:

```sh
curl -b cookies.txt -H 'X-AutoCanvas-Request: 1' -H 'Content-Type: application/json' \
  -d '{"address":"https://git.example.org/team/shop.git"}' \
  http://localhost:5080/api/v1/folders/<folder>/repositories
```

| Field | Meaning |
|---|---|
| `address` | The clone address, without a user name or password in it. |
| `name` | Its name in the folder; the last part of the address without `.git` when left out. |
| `defaultBranch` | The branch automations are read from; the repository's own default branch when left out. |
| `userName`, `secret` | For a private repository: a token (the user name may stay empty) or a user name and password. |

AutoCanvas reads the repository right away. One it cannot read is refused with git's reason, for
example a wrong credential or an address that does not exist.

The **credential** is stored encrypted under the master key, like a secret, and never shown again:
the API only says whether one is stored. Change it, the branch or the name with
`PATCH /api/v1/repositories/<id>`; an empty `secret` removes the credential.

## How automations follow the repository

- **Each file** in `.autocanvas/automations/` with the extension `.yml` or `.yaml` is one automation,
  named after the file without its extension. Files in subdirectories and other files are ignored.
- **Versions are commits.** When a file changed since the last read, the automation gets a new
  version: its message is the commit's subject, its author the commit's author, and the version
  remembers the commit.
- **Problems**: a file that is not a valid automation, or whose name another automation of the
  folder has already, is listed under `problems` of the repository, with what is wrong. An automation
  that had an earlier, valid version keeps it until the file is fixed.
- **Removed files** delete their automation; its runs stay, as for deleted managed automations.
- **Reading again**: `POST /api/v1/repositories/<id>/sync` reads the default branch now. When the
  repository cannot be read, nothing changes and `lastError` says why.

At most 100 automation files of at most 1 MiB each are read per repository.

## Who may do what

| Role on the folder | May |
|---|---|
| Viewer | see the folder's repositories and their state |
| Operator | also read a repository again (sync) |
| Editor | also connect, change and disconnect repositories |

## Disconnecting

`DELETE /api/v1/repositories/<id>` removes the repository from the folder and deletes its
automations; their runs stay. Nothing in the repository changes. A folder with connected
repositories cannot be deleted until they are disconnected.
