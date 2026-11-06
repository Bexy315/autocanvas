---
sidebar_position: 5
---

# Operations

Looking after a running server: its database and backups, the master key that protects secrets,
the first setup, the maintenance commands, runners and connected repositories, health checks and
logs.

- The settings themselves are in [Configuration](configuration.md).
- Container images, the compose package and reverse proxies are in [Deployment](deployment.md).
- A test instance that deploys itself is in [A dev instance of your own](dev-instance.md).

## Database

AutoCanvas stores its data in SQLite or PostgreSQL ([ADR-0010](../adr/0010-database-providers.md)).

| | SQLite | PostgreSQL |
|---|---|---|
| Setup | nothing: a file in the data directory | a PostgreSQL server (tested with version 17) and a database |
| Server instances | exactly one | one or more |
| Use it for | trying AutoCanvas, small single-server installations | production, teams, high availability |

Without any database settings the server uses SQLite and says so in its log. For PostgreSQL, create
an empty database and a user that owns it, then set:

```sh
AUTOCANVAS_Database__Provider=Postgres
AUTOCANVAS_Database__ConnectionString="Host=db;Database=autocanvas;Username=autocanvas;Password=…"
```

The connection string accepts every [Npgsql option](https://www.npgsql.org/doc/connection-string-parameters.html)
(SSL, timeouts, pooling). For SQLite it is a
[Microsoft.Data.Sqlite connection string](https://learn.microsoft.com/dotnet/standard/data/sqlite/connection-strings),
for example `Data Source=/var/lib/autocanvas/autocanvas.db`.

### Schema updates

By default the server applies pending schema migrations when it starts; with several PostgreSQL
instances starting at once, one applies them while the others wait. To control updates yourself,
set `Database:MigrateOnStart=false` and run the migrations before starting the new version:

```sh
dotnet AutoCanvas.Server.dll migrate     # same settings as the server, e.g. AUTOCANVAS_Database__…
```

It prints what it applied and exits with `0`, or `1` if the settings are invalid. While migrations
are missing, `/health/ready` answers `503`.

### Backups

Logs are the largest part of the database: up to `Logs:MaxBytesPerJob` per job attempt, kept as
long as their run (`Runs:RetentionDays`). Lower the retention, or the log limit, if the database
grows faster than wanted.

- **PostgreSQL**: `pg_dump` while the server runs.
- **SQLite**: the database runs in write-ahead-log mode, so copying `autocanvas.db` alone while the
  server runs is not safe. Stop the server and copy `autocanvas.db` with its `-wal` and `-shm`
  files, or use `sqlite3 autocanvas.db ".backup backup.db"` while it runs.
- **Master key**: back it up once, separately from the database backups (see [Master key](#master-key)).
  A database backup without the key restores everything except the secrets.

## Master key

Secrets are encrypted with a master key that is kept outside the database
([ADR-0013](../adr/0013-secrets-at-rest.md)), so a database dump or a copied SQLite file reveals no
secret.

**Without a configured key**, a fresh installation generates one on its first start, into
`keys/master.key` in the data directory, readable by the server's user only, and uses that file
from then on ([ADR-0020](../adr/0020-first-start-setup.md)). The setup shows the key once. Keep a copy
apart from the database, and better still move the file to a volume of its own and point
`AUTOCANVAS_MASTER_KEY_FILE` at it. A server whose database was set up with a key never generates
another one: without that key it does not start.

**To configure a key yourself**:

1. Create a key once: `dotnet AutoCanvas.Server.dll admin generate-master-key` prints 32 random
   bytes as base64 (44 characters).
2. Give it to the server as `AUTOCANVAS_MASTER_KEY`, or put it in a file readable only by the
   server and set `AUTOCANVAS_MASTER_KEY_FILE` to its path.
3. **Back it up separately from the database.** Without it, the secrets in a backup cannot be
   recovered; with it and a database backup, anyone can read them.

On its first start the server records which key it uses (by an id derived from the key, which
reveals nothing about it). If it is later started with another key, it stops with a message naming
both key ids, instead of failing runs later.

For local development, `dotnet run` uses a key of 32 zero bytes from `launchSettings.json`. It
protects nothing; never use it for an installation.

### Rotating the key

Rotate the key when it may have leaked, or as a routine:

1. Create a new key with `admin generate-master-key`.
2. Stop the server (every instance, with PostgreSQL).
3. Run `admin rotate-master-key --new-key-stdin` (or `--new-key-file <path>`) with the **current**
   key still configured. It re-encrypts every secret under the new key in one transaction; if any
   secret cannot be decrypted with the current key, it changes nothing and names it.
4. Configure the new key and start the server. The old key no longer works; keep the new one in
   your backup.

## Setting up a fresh installation

A server without accounts starts in **setup mode** ([ADR-0020](../adr/0020-first-start-setup.md)):

1. It logs a warning with the setup address, such as
   `http://localhost:5080/setup?token=…`, built from the public address if one is configured,
   otherwise from the address it listens on. The token changes with every start. On a server without
   a browser, open `/setup` on any address of the server from another machine and paste the token
   from the log; the page asks for it when the address has none.
2. Until setup is finished, the HTTP API answers only `/api/v1/setup` and `/api/v1/info`;
   everything else gets `503` with a [setup required](api.md#setup-required) problem. The web UI
   and the health checks work as usual.
3. The setup asks for the first admin, the public address and the runner address, whether to hand
   a registration token to the bundled runner (only with `Setup:RunnerTokenFile`), and whether to
   create an example automation. It creates all of it in one transaction and signs the admin in.
   If the server generated its master key, the setup shows it once.

Setup mode does not start when the bootstrap settings create the first admin, and it ends as soon
as any account exists, for example one created with `admin create`. `Setup:Enabled=false` turns it
off.

## Commands

The server executable also runs maintenance commands. They use the same settings as the server and
exit with `0` on success and `1` with a message on standard error otherwise.

| Command | Does |
|---|---|
| *(none)* | Runs the server |
| `migrate` | Applies pending schema migrations ([Schema updates](#schema-updates)) |
| `admin create <user-name> [--display-name <name>] [--password-stdin]` | Creates an admin; prints a one-time password unless one is read from standard input |
| `admin reset-password <user-name>` | Gives a user a new one-time password and ends their sessions |
| `admin generate-master-key` | Prints a new random master key ([Master key](#master-key)) |
| `admin rotate-master-key --new-key-stdin` or `--new-key-file <path>` | Re-encrypts every secret, repository credential, webhook secret and the GitHub App's key and secrets under a new master key; stop the server first ([Rotating the key](#rotating-the-key)) |

`admin` commands apply pending migrations first, unless `Database:MigrateOnStart` is `false`. Only
the server itself and `admin rotate-master-key` need the master key.

## Runners

Runners connect to an endpoint of their own that speaks only HTTP/2 (gRPC,
[ADR-0017](../adr/0017-runner-endpoint-and-credentials.md)), by default `http://localhost:5081`.
In a container, set `AUTOCANVAS_RUNNER_URLS=http://+:5081` and publish that port next to the API's.
Without TLS, runners connect with HTTP/2 directly (fine on a local network); for an `https://`
address, Kestrel needs a certificate as for the API. Whoever configures `Kestrel:Endpoints` directly
takes over both endpoints and must give the runner endpoint `"Protocols": "Http2"`.

Admins register runners with one-time tokens; [Running the reference runner](../user/runners.md#running-the-reference-runner)
shows how to start one next to the server with `docker run`. When the server
stops, connected runners are told to reconnect after five seconds; jobs they hold continue if they
are back within `Queue:LeaseDuration`.

## Repositories and forges

Connected repositories ([Repositories](../user/repositories.md),
[ADR-0021](../adr/0021-repositories-per-folder.md)) and the GitHub App
([GitHub](../user/github.md), [ADR-0022](../adr/0022-github-app.md)) need a few connections, a few
background rounds and a few secrets.

### Who reaches whom

| From | To | For |
|---|---|---|
| Server | each repository's address (http or https) | reading automation files, polling (`git ls-remote`, shallow fetches) |
| Server | GitHub's API (`https://api.github.com`, or `<webAddress>/api/v3` of a GitHub Enterprise Server) | the app's tokens, repositories, statuses, comments, pull requests |
| Runner's checkout container | each repository's address | checking out a run's commit |
| Forges (GitHub, Gitea, GitLab, …) | the server's **public address** (`/api/v1/hooks/acg_…`, `/api/v1/forges/github/events`) | webhooks; without them, polling brings the pushes |
| The admin's browser | GitHub | creating the app (`/api/v1/forges/github/manifest/launch` posts the manifest there) |

A repository's address must work from the server and from the runners' containers alike; an address
that only resolves on one of them (such as `localhost`) breaks checkouts.

### Reading repositories

The server reads with the `git` command line, which the container image includes; a server run from
a build needs `git` on its `PATH`. For each repository it keeps a shallow bare copy in
`Repositories:CacheDirectory`, removed when the repository is disconnected and refetched when
missing, so the directory needs no backup. git runs without prompts, with only `http` and `https`
allowed (also after redirects), and gets credentials as an HTTP header through its environment,
never on its command line or in a file. `Repositories:GitTimeout` limits each call.

### Background rounds

| Every | What | Notes |
|---|---|---|
| 30 s | **Polling** of due repositories (each its `pollMinutes`, 5 by default; `0` = off) with `git ls-remote` | at most 20 repositories a round, 50 moved refs each; a failed poll sets `lastError`, the next good one syncs |
| at once, else 5 s | **Stored pushes and pull requests** from webhooks (`repository_pushes`, `repository_pull_requests`) | webhook calls are answered at once; the server that took one is woken, others look every 5 s |
| 5 s | **Reporting** runs of GitHub repository automations (commit statuses, the pull request comment) | each change of a run's state once (`runs.reported_state`); a failed report is logged, not retried |

With several servers on one database each repository, stored event and run change is taken by one
of them, with a conditional update. The commit each branch and tag was last seen at is kept in the
database (`repository_refs`), so a restart starts no runs twice; GitHub deliveries are taken once
by their id (`github_deliveries`, three days). Installation tokens live in memory only, per server.
The app's events are limited to 600 a minute, a repository webhook address to `Webhooks:PerMinute`.

### Secrets

Repository credentials, repository webhook secrets and the GitHub App's private key, webhook secret
and client secret are encrypted under the master key, like secrets, and rotate with it
(`admin rotate-master-key`). None of them comes back through the API. Jobs get a GitHub token that
may only read the one repository, and pull requests from forks get no secrets
([Security](security.md#github-app)).

### Backups

Everything about repositories and the app is in the database; a database backup with the master key
restores it, and the cache directory is not needed. After restoring an older backup, polls see the
repositories' current state as new pushes since the backup and start their runs once.

### When something does not work

- **A repository's card** (folder → Repositories) shows the last read's error (`lastError`), such as
  a wrong credential or an address the server cannot reach, and the files that are not automations,
  with why. **Sync** reads again at once.
- **Pushes that start nothing**: is the repository polled, or has it a webhook (the card says how
  pushes arrive)? Does the file's `triggers.push` match the branch, and is the automation on the
  repository's branch? The server logs why a push started nothing, as a warning starting with
  *Repositories:*.
- **Webhook calls**: the forge's delivery log shows the answer: `401` for a wrong secret, `404` for
  an address that no longer exists, `200` with what happened.
- **GitHub**: **Administration → Forges** shows whether GitHub answers (installations) and the events
  address GitHub must reach; uninstalled or suspended installations mark their repositories. Reports
  GitHub refuses are logged as *Repositories:* warnings with GitHub's message.

## Health endpoints

| Endpoint | Answers `200` when | Use it for |
|---|---|---|
| `/health/live` | the process runs | container liveness probe; restart when it fails |
| `/health/ready` | the database is reachable and has every migration applied | load balancer / readiness probe; `503` while not ready |

Both return a short JSON document with the overall status and the status of each check, never
error details: those are in the log.

## Logging

The server logs to standard output as one JSON object per line (timestamp in UTC, level, category,
message and structured values), ready for any log collector. For reading logs by hand, set
`Logging__Console__FormatterName=simple` (the `Development` environment does this by default).
Log levels are set per category under `Logging:LogLevel`, for example
`Logging__LogLevel__Default=Debug`.
