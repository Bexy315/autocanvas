---
sidebar_position: 8.6
---

# GitHub

GitHub repositories are connected through a **GitHub App** of the instance
([ADR-0022](../adr/0022-github-app.md)). AutoCanvas reaches them with short-lived tokens of the app,
and GitHub sends the app's events, such as pushes, to one address. Once it is connected, a GitHub
repository works like any other [repository](repositories.md): its automations show in the folder,
pushes start runs, and jobs check out its commits.

:::note Status

Setting up the app works through the HTTP API so far; **Administration → Forges** in the web UI
follows next, and connecting repositories in a folder with the web UI later in S5. Pull requests,
commit statuses and comments come in the next step of S5.

:::

## Setting up the GitHub App

An admin sets up one app per instance. Its private key, webhook secret and client secret are
encrypted with the master key, like secrets, and are never shown again.

### Creating it from AutoCanvas

The instance needs a **public address** (**Administration** → **Instance**) that GitHub can reach:
GitHub sends the browser back there, and the app's events.

1. `POST /api/v1/forges/github/manifest` answers with an address on GitHub and a manifest, with
   `organization` to create the app in an organization rather than your own account, and
   `webAddress` for a GitHub Enterprise Server.
2. The browser posts the manifest to that address, as the form field `manifest`. GitHub shows the
   new app; its name can be changed there before **Create GitHub App**.
3. GitHub sends the browser back to `/admin/forges/github/created` with a code, which AutoCanvas
   turns into the app (`POST /api/v1/forges/github/created`). The code is valid for an hour.

The manifest asks for these permissions and events:

| Permission | Access | For |
|---|---|---|
| Contents | read and write | reading automation files, checkouts, and later branches for edits |
| Pull requests | read and write | pull request triggers and comments (next step) |
| Commit statuses | read and write | reporting runs on commits (next step) |
| Checks | read and write | reporting runs as checks (later) |
| Metadata | read | required by GitHub |

Events: **push** and **pull request**; GitHub sends installation events to every app.

### Entering an existing app

An app made on GitHub by hand (**Settings** → **Developer settings** → **GitHub Apps**) is entered
with `PUT /api/v1/forges/github`: its `appId`, a `privateKey` in PEM and its `webhookSecret`, and for
a GitHub Enterprise Server `webAddress` (and `apiAddress` if it is not `<webAddress>/api/v3`).
AutoCanvas asks GitHub whether the id and key belong together. The app's webhook must point to
`<public address>/api/v1/forges/github/events`, with content type `application/json`.

The same call enters a **new private key** or webhook secret: make one on GitHub, enter it, then
delete the old one there.

### Installing it

`GET /api/v1/forges/github` shows the app, where it is installed and `installUrl`, where it is
installed on another account or organization. There, choose all repositories or some. AutoCanvas sees
only the repositories of its installations.

`DELETE /api/v1/forges/github` removes the app from AutoCanvas once no GitHub repository is
connected; on GitHub, it stays until it is deleted there.

## Connecting a repository

Editors of a folder see what the app reaches with
`GET /api/v1/folders/<folder>/github/repositories` and connect one:

```sh
curl -b cookies.txt -H 'X-AutoCanvas-Request: 1' -H 'Content-Type: application/json' \
  -d '{"installationId": 12345678, "repository": "acme/shop"}' \
  http://localhost:5080/api/v1/folders/<folder>/repositories/github
```

`name`, `defaultBranch` and `pollMinutes` work as for any repository; the name is the repository's
by default. A GitHub repository has no credential of its own: AutoCanvas gets an installation token
of the app (valid an hour, kept until shortly before it expires) for reading it and for each job's
checkout, where it is masked in the log like a secret.

## Pushes

GitHub sends every push of an installed repository to the app's address, signed with its webhook
secret; AutoCanvas takes each delivery once, and the push starts its runs within seconds, as
described under [Pushes](repositories.md#pushes). A repository connected in several folders runs
its automations in each. Polling still works alongside (`pollMinutes`, 5 by default), and is what
remains when GitHub cannot reach the instance; with the events arriving, it can be turned off with
`pollMinutes: 0`.

When the app is uninstalled from an account, or its installation is suspended, the account's
repositories say so in `lastError`.
