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

Admins set up the app under **Administration → Forges**. Connecting its repositories in a folder
works through the HTTP API so far; the web UI for that follows later in S5. Pull requests, commit
statuses and comments come in the next step of S5.

:::

## Setting up the GitHub App

An admin sets up one app per instance. Its private key, webhook secret and client secret are
encrypted with the master key, like secrets, and are never shown again.

### Creating it from AutoCanvas

The instance needs a **public address** (**Administration** → **Instance**) that GitHub can reach:
GitHub sends the browser back there, and the app's events.

![Administration, Forges, without an app](img/github-create-light.png#gh-light-mode-only)
![Administration, Forges, without an app](img/github-create-dark.png#gh-dark-mode-only)

1. Under **Administration → Forges**, name the **Organization** to create the app in, or leave it
   empty for your own GitHub account; for a GitHub Enterprise Server, give its address under
   **GitHub Enterprise Server**.
2. **Create GitHub App** takes you to GitHub with the app's description. Look it over there; its
   name can be changed (it must be unique on GitHub). Create it.
3. GitHub sends you back, and AutoCanvas takes over the app with its key and secrets. This has to
   happen within an hour of step 2.

![Administration, Forges, with the app](img/github-app-light.png#gh-light-mode-only)
![Administration, Forges, with the app](img/github-app-dark.png#gh-dark-mode-only)

Through the HTTP API, `POST /api/v1/forges/github/manifest` (with `organization` and `webAddress`)
answers with the address on GitHub and the manifest to post there as the form field `manifest`, and
`POST /api/v1/forges/github/created` takes the `code` and `state` GitHub hands back.

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

**Enter an existing app** (or `PUT /api/v1/forges/github`) takes an app made on GitHub by hand (**Settings** → **Developer settings** → **GitHub Apps**): its
App ID, a private key (the `.pem` file GitHub gives you, or its text) and its webhook secret, and for
a GitHub Enterprise Server its address (through the API also `apiAddress`, if it is not
`<webAddress>/api/v3`).
AutoCanvas asks GitHub whether the id and key belong together. The app's webhook must point to
`<public address>/api/v1/forges/github/events`, with content type `application/json`.

**Enter a new key or secret** takes a new private key or webhook secret: make one on GitHub, enter
it, then delete the old one there.

### Installing it

**Administration → Forges** shows where the app is installed; **Install on an account** opens
GitHub to install it on another account or organization (`installUrl` of
`GET /api/v1/forges/github`). There, choose all repositories or some. AutoCanvas sees only the
repositories of its installations.

**Remove** (`DELETE /api/v1/forges/github`) removes the app from AutoCanvas once no GitHub repository
is connected; on GitHub, it stays until it is deleted there.

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
