---
sidebar_position: 1.2
---

# Installation

AutoCanvas runs with Docker: the server with its web UI and a runner that runs jobs in containers,
as a `docker compose` package. An install script sets it up in a directory of its own; the rest
happens in the browser.

:::note Before the first release

Until 0.1 the install script builds the images from a checkout of the repository, which takes a few
minutes the first time. Images of every commit on `main` are published too
([Using the published images](#using-the-published-images)), but they stay private until the
first release.

:::

## What you need

- **Docker** with **Compose v2** (`docker compose`): Docker Engine on Linux, or Docker Desktop on
  Windows and macOS. The user running the script must be allowed to use Docker.
- **Ports** 5080 (web UI and API) and 5081 (for runners on other machines); both can be changed.
- About 2 GB of disk for the images, more for the jobs' images.

## Install

From a checkout, on Linux or macOS:

```sh
deploy/install.sh --dir ~/autocanvas
```

On Windows, in PowerShell 7:

```powershell
deploy/install.ps1 -Dir C:\autocanvas
```

| Option | `install.sh` | `install.ps1` | Default |
|---|---|---|---|
| The instance's directory | `--dir` | `-Dir` | `./autocanvas` |
| PostgreSQL instead of SQLite | `--postgres` | `-Postgres` | SQLite |
| Port of the web UI and API | `--port` | `-Port` | 5080 |
| Port for other runners | `--runner-port` | `-RunnerPort` | 5081 |

The script builds the images, puts `compose.yaml` and its `.env` into the directory, starts the
server and the runner, and ends with the setup token:

```
AutoCanvas is running in /home/ada/autocanvas.
Open http://<this machine's address>:5080/setup in a browser and enter the setup token:

  <the setup token: 43 letters, digits, - and _>
```

## Set it up in the browser

Open `/setup` on the machine's address, from any computer that reaches it, and enter the token.

![The setup asks for the token from the log](img/install-1-token-light.png#gh-light-mode-only)
![The setup asks for the token from the log](img/install-1-token-dark.png#gh-dark-mode-only)

The setup asks for the first admin, then the addresses (filled in from the browser) and two
options, and shows the master key once ([Users and groups](accounts.md#the-first-admin)).

![The first admin](img/install-2-admin-light.png#gh-light-mode-only)
![The first admin](img/install-2-admin-dark.png#gh-dark-mode-only)


- **Register the bundled runner**: the runner of the package registers itself and runs jobs right
  away, with the labels `linux` and `docker`.
- **Create an example automation**: a folder *Getting started* with an automation `hello`. Start it
  with **Run** to see a log, tests and coverage.

![The options](img/install-3-options-light.png#gh-light-mode-only)
![The options](img/install-3-options-dark.png#gh-dark-mode-only)

![The master key, shown once](img/install-4-master-key-light.png#gh-light-mode-only)
![The master key, shown once](img/install-4-master-key-dark.png#gh-dark-mode-only)


**Keep the master key** somewhere safe, apart from the server: without it, the secrets in a backup
cannot be read again. The token changes when the server restarts; `docker compose logs server` in the
instance's directory shows the current one.

When the bundled runner is online, AutoCanvas is ready.

![Set up, with the runner online](img/install-5-done-light.png#gh-light-mode-only)
![Set up, with the runner online](img/install-5-done-dark.png#gh-dark-mode-only)

## Your first run

**Open the example** leads to the automation `hello`. On its **Runs** tab, **Run** starts it on the
bundled runner: the run page follows the log live, and the tabs **Tests** and **Coverage** show its
small reports. [A tour of the web UI](web-ui.md) shows the rest.

![The first run of the example](img/install-6-first-run-light.png#gh-light-mode-only)
![The first run of the example](img/install-6-first-run-dark.png#gh-dark-mode-only)

## Updating

Pull the new version of the checkout and run the script again with the same directory. It builds the
images and restarts the containers; the `.env` and the data are kept.

## Backups

The instance keeps its state in Docker volumes named after its directory (`autocanvas_` by default):

| Volume | Holds |
|---|---|
| `…_data` | The SQLite database and the server's files |
| `…_keys` | The master key — back it up **separately** from the data |
| `…_postgres` | The PostgreSQL database, with `--postgres` |
| `…_runner` | The runner's credential and the jobs' workspaces |
| `…_handoff` | The token the setup handed to the runner; not needed after the setup |

For a consistent copy, stop the server first (`docker compose stop server`), or use `pg_dump` with
PostgreSQL. [Operations](../technical/operations.md#backups) has the details.

## Without the script

Everything the script does can be done by hand, for example to adapt the package:

1. Build the images from the checkout:
   `docker build -f src/AutoCanvas.Server/Dockerfile -t autocanvas-server:local --build-arg SOURCE_REVISION=$(git rev-parse HEAD) .`
   and the same with `src/AutoCanvas.Runner/Dockerfile` and `autocanvas-runner:local`. The
   revision is optional; with it, the web UI shows which commit runs. Or use the published images
   (below).
2. Copy `deploy/compose/compose.yaml` (and `compose.postgres.yaml` for PostgreSQL) and
   `.env.example` into a directory, and rename `.env.example` to `.env`.
3. Fill in `.env`: its comments say how to find the Docker socket's group and, after
   `docker compose create`, where the runner's volume is on the Docker host.
4. `docker compose up -d`, then `docker compose logs server` for the setup address.

Behind a reverse proxy, set `AUTOCANVAS_TRUSTED_PROXIES` in `.env` to the proxy's address
([Operations](../technical/deployment.md#reverse-proxies-and-https)).

## Using the published images

Every commit on `main` is published to the GitHub Container Registry:
- `ghcr.io/bexy315/autocanvas-server`
- `ghcr.io/bexy315/autocanvas-runner`

Each has the tags `sha-<commit>` (the full commit) and `dev` (the newest on `main`). They are not
releases: `dev` changes with every merge, so pin a `sha-` tag for anything you want to keep.

To run them, follow [Without the script](#without-the-script) and skip the build. In `.env`, name
the images instead of the local ones:

```sh
AUTOCANVAS_SERVER_IMAGE=ghcr.io/bexy315/autocanvas-server:sha-<commit>
AUTOCANVAS_RUNNER_IMAGE=ghcr.io/bexy315/autocanvas-runner:sha-<commit>
```

To update, change the tags (or keep `dev`) and run `docker compose pull` and `docker compose up -d`.
Until the first release the images are private: sign in first with `docker login ghcr.io`, using a
GitHub token with the `read:packages` scope.
