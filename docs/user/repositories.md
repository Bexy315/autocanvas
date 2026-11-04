---
sidebar_position: 8.5
---

# Repositories

Automations can live in a Git repository, next to the code they build
([ADR-0003](../adr/0003-pipeline-sources.md)). A repository is connected in a folder
([ADR-0021](../adr/0021-repositories-per-folder.md)): its files `.autocanvas/automations/*.yml` (or
`.yaml`) become **repository automations** of that folder, next to the managed ones.

:::note Status

Repositories are connected through the HTTP API so far; the web UI follows later in S5, as do
pull request triggers and GitHub.

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
| `pollMinutes` | How often AutoCanvas asks the repository for new commits, 1 to 1440 minutes; `0` not to. Default: 5. |

AutoCanvas reads the repository right away. One it cannot read is refused with git's reason, for
example a wrong credential or an address that does not exist.

The **credential** is stored encrypted under the master key, like a secret, and never shown again:
the API only says whether one is stored. Change it, the branch, the name or `pollMinutes` with
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

## Read-only in AutoCanvas

A repository automation is changed in its repository, not in AutoCanvas: saving, renaming, moving or
deleting it is refused. Its file can still be checked, and it runs, triggers and reports like any
other automation. Variables, secrets and roles come from its folder.

## Runs and checkouts

A run of a repository automation keeps the commit its file was read at (`commit` and `ref` of the
run). Its jobs check out that commit into their workspace by default, with the repository's
credential; `checkout: false` turns that off. In a list of checkouts:

- `self` is the automation's own repository, at the run's commit unless `ref` says otherwise;
- the **name of a repository** connected in the run's folder or a folder above it (the nearest wins,
  and a restricted folder cuts the chain as for variables and secrets) is checked out at its branch,
  with its credential;
- an **https address** is checked out without a credential.

The credential reaches only the runner of that job, as part of the checkout, and is masked in the
log like a secret.

## Pushes

A push to the repository starts the runs that the automations' `push` triggers ask for:

```yaml
triggers:
  push:
    branches: [main, 'release/*']   # left out: every branch
    tags: ['v*']                    # left out: no tag
    paths: ['src/**', 'build.sh']   # left out: any change
```

- **Patterns**: `*` matches any characters but `/`, `**` also `/` (and `**/` no folder at all), `?`
  one character but `/`. A pattern matches the whole name: `release/*` matches `release/2.0`, not
  `release/2.0/fix`.
- **Paths** count when AutoCanvas knows the changed files: those between the commit before and
  after the push, if the older one is at most 50 commits back on the same line of history. For a new
  branch, a forced push or one further back, any change counts.
- **Which file runs**: the automation's file **at the pushed commit**, so a branch can try out a
  changed automation before it is merged. Its triggers there decide, and the run checks out that
  commit. Only automations the folder has, that is files on the repository's branch, run: a file
  that exists only on another branch starts nothing until it is merged.
- **The repository's branch**: a push to it first reads the automations again (as a sync does), so
  the run is of the new version. Pushes to other branches and tags create no versions.
- **Once per push**: each new commit of a branch or tag starts its runs once, however AutoCanvas
  learns of it. Deleting a branch or tag starts nothing; what was there when the repository was
  connected starts nothing either.
- **What the run reads**: `trigger.event` is `push`; `trigger.branch` (or `trigger.tag`),
  `trigger.ref` (`refs/heads/main`), `trigger.sha`, `trigger.short_sha`, `trigger.before`,
  `trigger.pusher` and `trigger.message` describe the push, `run.branch`, `run.commit_sha` and
  `run.commit_short_sha` the run's commit. The run is started by the pusher, or by the commit's
  author when the pusher is not known.

### Webhook

With a webhook, the repository's forge tells AutoCanvas of each push, so runs start within seconds.
Editors of the folder create the address:

```sh
curl -b cookies.txt -H 'X-AutoCanvas-Request: 1' -X POST \
  http://localhost:5080/api/v1/repositories/<id>/webhook
```

The answer has the address (`url`, from the public address under **Administration** → **Instance**,
or `path`) and the `secret`. Both are shown **only once**; a new address replaces the old one, and
`DELETE /api/v1/repositories/<id>/webhook` removes it. The repository shows the address's `hint`.
Enter both at the forge:

| Forge | Where | Settings |
|---|---|---|
| GitHub | the repository's **Settings** → **Webhooks** → **Add webhook** | Payload URL = the address, content type `application/json`, Secret = the secret, event **Just the push event** |
| Gitea, Forgejo | **Settings** → **Webhooks** → **Add webhook** → **Gitea** | Target URL = the address, POST, content type `application/json`, Secret = the secret, trigger **Push events** |
| GitLab | **Settings** → **Webhooks** → **Add new webhook** | URL = the address, Secret token = the secret, trigger **Push events** and **Tag push events** |

The server must be reachable from the forge at that address. A call that does not prove the secret
(`X-Hub-Signature-256` or `X-Gitea-Signature`, an HMAC-SHA256 of the body, or `X-Gitlab-Token`)
is refused with `401`. GitHub's test call (`ping`) and other events are answered with `200` and do
nothing; a push is answered with `200` at once and its runs start in the background. When a
webhook and a poll both see a push, it starts its runs once, so polling can stay on as a safety
net, or be turned off (`pollMinutes: 0`).

The changed files for `paths` come from the push's commits; for pushes of 20 commits or more, a
new branch or a forced push, AutoCanvas finds them in the repository as when polling.

### Polling

AutoCanvas asks each repository for its branches and tags every `pollMinutes` minutes (5 by
default) and handles what moved as pushes, so pushes start runs with a delay of up to that long.
A poll that fails sets `lastError`; the next one that works reads the repository again and clears
it. `nextPollAt` on the repository says when it is asked next.

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
