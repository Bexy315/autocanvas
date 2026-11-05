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

Admins set up the app under **Administration → Forges**; editors connect its repositories in a
folder's **Repositories** tab and propose changes of their automations from the editor.

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
| Pull requests | read and write | pull request triggers and the comment with the results |
| Commit statuses | read and write | each run's result on its commit |
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

Editors of a folder choose one under **Repositories** → **Connect a repository** → **GitHub**, which
lists what the app's installations reach ([Repositories](repositories.md#in-the-web-ui)).

![Connecting a repository from the GitHub App](img/github-connect-light.png#gh-light-mode-only)
![Connecting a repository from the GitHub App](img/github-connect-dark.png#gh-dark-mode-only)

Through the API, `GET /api/v1/folders/<folder>/github/repositories` lists them and this connects
one:

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

## Pull requests

GitHub sends the app's pull request events too: a pull request that is opened, gets new commits,
is reopened, becomes ready for review or is edited starts the runs its automations'
[`pull_request` triggers](triggers.md#pull-requests) ask for, with the files at its head. Closing
one starts nothing. Runs of pull requests from forks get no secrets.

A job checks out a GitHub repository with a token that may only read that repository, whatever
started the run.

## Proposing changes

A repository automation is changed in its repository, so AutoCanvas does not save it. For a GitHub
repository, an editor's change becomes a **pull request** instead: in the editor, **Propose** opens a
dialog for an optional title and commit message, and **Open pull request** links to it afterwards
(`POST /api/v1/automations/<id>/pull-request` with the new `yaml`, and optionally `title` and
`message`):

![Proposing a change as a pull request](img/github-propose-light.png#gh-light-mode-only)
![Proposing a change as a pull request](img/github-propose-dark.png#gh-dark-mode-only)

1. A branch `autocanvas/<automation>-<time>` is made from the commit of the automation's current
   version.
2. The new file is committed on it, as the app, with the message (by default
   *Change &lt;automation&gt; in AutoCanvas*).
3. A pull request into the repository's branch is opened, saying who proposed it and linking to the
   automation.

The file must be valid, as when saving. The automation in AutoCanvas changes once the pull request
is merged and the push arrives. When GitHub refuses (for example a protected branch pattern or a
missing permission of the app), the answer says why.

## Results on GitHub

Every run of an automation from a GitHub repository shows on its commit, whatever started it:

- **A commit status** named `AutoCanvas / <automation> (<event>)`, such as
  `AutoCanvas / build (push)` or `AutoCanvas / build (pull_request)`: *pending* while the run runs,
  then *success*, *failure* or *error* (cancelled). Its description says how long the run took and
  how many tests failed; with a public address it links to the run. A push and a pull request of the
  same commit have statuses of their own. Branch protection on GitHub can require them.
- **A comment on the pull request**, for runs of a pull request: one per pull request, with the latest
  run of each automation for its head commit, its result, tests and line coverage. Later runs update
  it; if someone deletes it, the next result writes a new one.

The server reports within seconds of a run's start and end. A report GitHub refuses (for example
when the app was uninstalled) is logged on the server and not tried again; the run itself is not
affected.

When the app is uninstalled from an account, or its installation is suspended, the account's
repositories say so in `lastError`.
