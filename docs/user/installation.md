---
sidebar_position: 1.2
---

# Installation

AutoCanvas runs with Docker: the server with its web UI and a runner that runs jobs in containers,
as a `docker compose` package. An install script sets it up in a directory of its own; the rest
happens in the browser.

:::note Before the first release

Until 0.1 there are no published images: the install script builds them from a checkout of the
repository, which takes a few minutes the first time.

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

  fU7n8dJW3ra7sVgk2v144SR4nD3y5juacUlYCIS-m4M
```

## Set it up in the browser

Open `/setup` on the machine's address, from any computer that reaches it, and enter the token. The
setup asks for the first admin, the addresses and two options, then shows the master key once
([Users and groups](accounts.md#the-first-admin)):

- **Register the bundled runner**: the runner of the package registers itself and runs jobs right
  away, with the labels `linux` and `docker`.
- **Create an example automation**: a folder *Getting started* with an automation `hello`. Start it
  with **Run** to see a log, tests and coverage.

**Keep the master key** somewhere safe, apart from the server: without it, the secrets in a backup
cannot be read again. The token changes when the server restarts; `docker compose logs server` in the
instance's directory shows the current one.

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
   `docker build -f src/AutoCanvas.Server/Dockerfile -t autocanvas-server:local .` and the same with
   `src/AutoCanvas.Runner/Dockerfile` and `autocanvas-runner:local`.
2. Copy `deploy/compose/compose.yaml` (and `compose.postgres.yaml` for PostgreSQL) and
   `.env.example` into a directory, and rename `.env.example` to `.env`.
3. Fill in `.env`: its comments say how to find the Docker socket's group and, after
   `docker compose create`, where the runner's volume is on the Docker host.
4. `docker compose up -d`, then `docker compose logs server` for the setup address.

Behind a reverse proxy, set `AUTOCANVAS_TRUSTED_PROXIES` in `.env` to the proxy's address
([Operations](../technical/operations.md#reverse-proxies-and-https)).
