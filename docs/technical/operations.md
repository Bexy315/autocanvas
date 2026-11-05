---
sidebar_position: 5
---

# Operations

How to configure and observe the AutoCanvas server. This page grows with the server.

## Configuration

The server reads its settings from these sources; a later source wins over an earlier one:

1. `appsettings.json` next to the server
2. `appsettings.{Environment}.json` (environment from `ASPNETCORE_ENVIRONMENT`, default
   `Production`)
3. environment variables starting with `AUTOCANVAS_`
4. command-line arguments (`--Server:PublicUrl=https://…`)

Environment variables follow the .NET convention: `AUTOCANVAS_` followed by the setting's path with
`__` (two underscores) between the parts. `Server:PublicUrl` becomes `AUTOCANVAS_Server__PublicUrl`.

A few frequently used settings also have a short name. If both forms are set, the long form wins.

| Short name | Setting | Meaning |
|---|---|---|
| `AUTOCANVAS_URLS` | `urls` | Addresses the HTTP API listens on, e.g. `http://+:8080` |
| `AUTOCANVAS_RUNNER_URLS` | `Runners:Urls` | Addresses runners connect to, e.g. `http://+:5081` (see [Runners](#runners)) |
| `AUTOCANVAS_BOOTSTRAP_ADMIN_USERNAME` | `Bootstrap:AdminUserName` | First admin, created on start while there is no account |
| `AUTOCANVAS_BOOTSTRAP_ADMIN_PASSWORD` | `Bootstrap:AdminPassword` | That admin's initial password; must be changed at the first sign-in |
| `AUTOCANVAS_MASTER_KEY` | `Secrets:MasterKey` | The master key that encrypts secrets (see [Master key](#master-key)) |
| `AUTOCANVAS_MASTER_KEY_FILE` | `Secrets:MasterKeyFile` | A file holding the master key instead, e.g. a container secret |
| `AUTOCANVAS_TRUSTED_PROXIES` | `Server:TrustedProxies` | Reverse proxies whose forwarded headers are believed (see [Reverse proxies and HTTPS](#reverse-proxies-and-https)) |
| `AUTOCANVAS_SETUP_RUNNER_TOKEN_FILE` | `Setup:RunnerTokenFile` | Where the setup hands a registration token to the bundled runner (see [Setting up a fresh installation](#setting-up-a-fresh-installation)) |

Settings are checked when the server starts. An invalid value stops it with a message that names the
setting and what is expected, instead of failing later on first use.

| Setting | Default | Meaning |
|---|---|---|
| `Server:PublicUrl` | none | Absolute `http`/`https` URL users reach the server at, if it differs from what the server sees behind a reverse proxy. Set here, it wins over the address set in the web UI (see [Instance settings](#instance-settings)) |
| `Runners:PublicUrl` | none | Absolute `http`/`https` URL runners reach the runner endpoint at, shown in the commands to register one; wins over the web UI's |
| `Setup:Enabled` | `true` | Whether a server without accounts opens the setup; `false` leaves the first admin to `admin create` and the bootstrap settings |
| `Setup:RunnerTokenFile` | none | A file the setup writes a registration token to, for a runner that reads it from a shared volume |
| `Repositories:CacheDirectory` | `repositories` in the data directory | Where the server keeps a shallow copy of each connected repository (see [Repositories and forges](#repositories-and-forges)) |
| `Repositories:GitTimeout` | `00:01:00` | How long one git call of the server may take, 5 seconds to 10 minutes |
| `Server:TrustedProxies` | none | Reverse proxies whose `X-Forwarded-For` and `X-Forwarded-Proto` are believed: addresses or networks (`10.0.0.0/8`), separated by commas (see [Reverse proxies and HTTPS](#reverse-proxies-and-https)) |
| `Server:DataDirectory` | `data` | Where the server keeps its files, such as the default SQLite database; relative to the server's directory |
| `Database:Provider` | `Sqlite` | `Sqlite` or `Postgres` (see [Database](#database)) |
| `Database:ConnectionString` | SQLite file `autocanvas.db` in the data directory | Required for PostgreSQL |
| `Database:MigrateOnStart` | `true` | Bring the database schema up to date when the server starts |
| `Authentication:SessionIdleTimeout` | `7.00:00:00` | A session ends after this long without requests |
| `Authentication:SessionLifetime` | `30.00:00:00` | A session ends this long after the sign-in, however active |
| `Bootstrap:AdminUserName`, `Bootstrap:AdminPassword` | none | Create this admin on start while there is no account at all; set both or neither (see [Users and groups](../user/accounts.md#the-first-admin)) |
| `Runners:Urls` | `http://localhost:5081` | Addresses of the runner endpoint, HTTP/2 only; separate several with `;` (see [Runners](#runners)) |
| `Runners:HeartbeatInterval` | `00:00:20` | How often runners send a heartbeat; at least one second and at most half of `Queue:LeaseDuration` |
| `Logs:MaxBytesPerJob` | `67108864` (64 MiB) | Log text one job attempt may store, 1 MiB to 1 GiB; later lines are dropped (see [Runs](../user/runs.md#logs)) |
| `Reports:MaxBytesPerFile` | `52428800` (50 MiB) | Largest report file a runner may upload, 1 MiB to 1 GiB (see [Reports](../user/reports.md#limits)) |
| `Reports:MaxFilesPerJob` | `100` | Report files one job attempt may upload, 1 to 1000 |
| `Webhooks:PerMinute` | `30` | Calls one webhook address takes per minute, 1 to 10,000; more are answered with 429 (see [Triggers](../user/triggers.md#webhooks)) |
| `Runs:RetentionDays` | `90` | Delete finished runs older than this many days, with their logs and reports, once a day; `0` keeps them forever (see [Runs](../user/runs.md#how-long-runs-are-kept)) |
| `Runs:KeepPerAutomation` | `20` | The newest runs of each automation, kept however old they are; 0 to 1000 |
| `Queue:LeaseDuration` | `00:01:00` | How long a runner holds a job after its last heartbeat (10 seconds to 1 hour); then an offered job goes back to the queue and a running one fails as if the runner were gone |
| `Audit:RetentionDays` | `365` | Delete audit entries older than this many days, once a day; `0` keeps them forever (see [Audit log](audit.md#retention)) |
| `Secrets:MasterKey` or `Secrets:MasterKeyFile` | `keys/master.key` in the data directory, generated on the first start | The master key, or a file holding it; at most one of them (see [Master key](#master-key)) |

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

## Instance settings

The public address and the runner address can be set in the web UI (**Administration** →
**Instance**) and through `GET`/`PUT /api/v1/instance`; they are kept in the database. When `Server:PublicUrl` or
`Runners:PublicUrl` is set in the configuration, that value wins and the web UI shows it as locked.
The public address decides, with the request, whether the session cookie is marked `Secure`.

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

## Reverse proxies and HTTPS

The session cookie is marked `Secure` when the server sees an HTTPS request or when the public
address starts with `https://`. Behind a proxy that ends TLS, set the public address to the public
HTTPS address (**Administration** → **Instance**, or `Server:PublicUrl`).

**Trusted proxies**: set `Server:TrustedProxies` (`AUTOCANVAS_TRUSTED_PROXIES`) to the proxy's
address or network, such as `172.16.0.0/12` for a proxy in a Docker network. The server then believes
the `X-Forwarded-For` and `X-Forwarded-Proto` headers that come from there: sign-in and setup limits
count per client, the audit log and the session list show the client's address, and a request the
proxy took over HTTPS counts as HTTPS. The proxy must set those headers and overwrite any a client
sent. Without trusted proxies the headers are ignored, and all requests through a proxy count as
one address.

Webhook calls (`POST /api/v1/hooks/…`) come from other systems; a proxy in front of the API must
let request bodies of up to 1 MiB through for them, and should not log request paths, which hold
the webhook tokens.

Following a log live (`…/log/stream`) is a long-lived HTTP response of Server-Sent Events; a proxy
in front of the API must not buffer it (nginx: the server sends `X-Accel-Buffering: no`; others:
turn response buffering off for that path) and must allow it to stay open — the server sends a
comment every 15 seconds while nothing happens.

A proxy in front of the runner endpoint must forward gRPC: HTTP/2 to the server, with trailers, and
without buffering or timing out long-lived streams. With nginx this is `grpc_pass`, with Caddy
`reverse_proxy` with `transport http { versions h2c }`, with Traefik a service using `h2c`.

## Web UI

The server serves the web UI on the same address as the HTTP API, from its web root (`wwwroot` next
to the server; the container image contains it). There is nothing to configure: open the server's
address in a browser. Behind a reverse proxy, forward every path to the server, not only `/api`,
and keep the headers the server sets (`Content-Security-Policy`, `Cache-Control`); see
[Web UI](web-ui.md) for what is cached how. A server built without the UI answers the UI's paths
with `404` and works otherwise as usual.

## Container image

`src/AutoCanvas.Server/Dockerfile` builds the server with its web UI (from the repository root:
`docker build -f src/AutoCanvas.Server/Dockerfile -t autocanvas-server .`). It listens on 5080 (web
UI and API) and 5081 (runners), keeps its data in the volume `/var/lib/autocanvas` and runs as a
non-root user.

The build argument `SOURCE_REVISION` (a commit) goes into the server's version, which
`GET /api/v1/info` and the web UI's footer show. The build context has no `.git` to read it from.
The runner's Dockerfile takes the same argument.

CI builds both images on every change and checks that the server names its commit
(`deploy/test/image-version.sh`). The *Images* workflow (`.github/workflows/images.yml`) publishes
every commit on `main` to the GitHub Container Registry:
- `ghcr.io/bexy315/autocanvas-server` and `-runner`;
- tagged `sha-<commit>` and `dev`, for `linux/amd64`;
- private while the repository is ([ADR-0023](../adr/0023-published-images-and-dev-instance.md)).

Releases come with 0.1.

```sh
docker run -d --name autocanvas -p 5080:5080 -p 5081:5081 -v autocanvas-data:/var/lib/autocanvas \
  -e AUTOCANVAS_MASTER_KEY=… -e AUTOCANVAS_BOOTSTRAP_ADMIN_USERNAME=admin \
  -e AUTOCANVAS_BOOTSTRAP_ADMIN_PASSWORD='a first password' autocanvas-server
```

Without the master key and the bootstrap settings, the server generates a key into
`/var/lib/autocanvas/keys` (a volume of its own in the compose package) and opens the setup.

## Compose package

`deploy/compose/` holds the package that `deploy/install.sh` and `deploy/install.ps1` install
([Installation](../user/installation.md)):

- **`compose.yaml`**: the server (ports 5080 and 5081; volumes for the data, the generated master key
  and the handoff to the runner) and the bundled runner. The runner gets the Docker socket and the
  socket's group (`AUTOCANVAS_DOCKER_GID`), registers itself with the token the setup writes to the
  shared `handoff` volume, and announces `linux` and `docker`. Its data lives in a volume; job
  containers mount their workspaces from that volume's path on the Docker host
  (`AUTOCANVAS_RUNNER_HOST_DATA`, from `docker volume inspect`), which works the same with Docker
  Engine and Docker Desktop.
- **`compose.postgres.yaml`**: adds PostgreSQL 17 and points the server at it; chosen through
  `COMPOSE_FILE` in `.env`.
- **`.env.example`**: every variable with a comment.

The install script writes `.env` only once (mode 600), so running it again updates the images and
keeps the settings. CI installs the package with the script, sets it up through the API and runs the
example automation on the bundled runner, with SQLite and with PostgreSQL
(`deploy/test/compose-smoke.sh`).

### Backing up the package

The state lives in volumes named after the compose project (`COMPOSE_PROJECT_NAME` in `.env`,
`autocanvas` by default). From the instance's directory:

```sh
# The master key, once and separately from everything else:
docker run --rm -v autocanvas_keys:/from:ro -v "$PWD":/to alpine:3.22 cp /from/master.key /to/master.key

# SQLite: stop the server for a consistent copy of the data volume.
docker compose stop server
docker run --rm -v autocanvas_data:/from:ro -v "$PWD":/to alpine:3.22 tar -czf /to/data.tar.gz -C /from .
docker compose start server

# PostgreSQL: a dump while it runs.
docker compose exec -T postgres pg_dump -U autocanvas autocanvas > autocanvas.sql
```

To restore, create the instance with the install script, stop it, put the files back into the
volumes the same way (or `psql` the dump into the `postgres` service), and start it. The runner's
volume needs no backup: a runner can be registered again.

### Updating the package

Run the install script again with the same directory. It builds the images from the current checkout
and `docker compose up -d` recreates the containers whose image changed. With the published images,
change their tags in `.env`, then run `docker compose pull` and `docker compose up -d`
([Installation](../user/installation.md#using-the-published-images)). Either way, schema migrations
run when the server starts (`Database:MigrateOnStart`). Running jobs end when the runner restarts
and are retried by their run's rules.

### A proxy in front

To serve AutoCanvas under a name with HTTPS, put a reverse proxy in front of port 5080, for example
Caddy in the same compose project (`compose.override.yaml` next to `compose.yaml`):

```yaml
services:
  caddy:
    image: caddy:2.10-alpine
    restart: unless-stopped
    ports: ["443:443", "80:80"]
    command: caddy reverse-proxy --from autocanvas.example.org --to server:5080
```

Then set the public address to `https://autocanvas.example.org` under **Administration** →
**Instance**, and `AUTOCANVAS_TRUSTED_PROXIES` in `.env` to the compose network (for example
`172.16.0.0/12`) so the server sees the clients' addresses. Runners on other machines keep using port
5081, or a gRPC route of the proxy ([Reverse proxies and HTTPS](#reverse-proxies-and-https)).

## A dev instance of your own

A throwaway instance for manual tests:
- it runs the newest `main` of your repository or fork;
- it resets itself every night to a seed of test users, folders and runs;
- it is reachable through Cloudflare Access only.

[ADR-0023](../adr/0023-published-images-and-dev-instance.md) explains the choices. Everything is in
the repository's `deploy/dev` (its README describes each file) and `.github/workflows/dev.yml`;
nothing in them names a domain or a host.

To set one up:
1. **Images**: the *Images* workflow publishes `main` to GHCR under the repository's owner
   ([Container image](#container-image)).
2. **Host**: a fresh Ubuntu 24.04 VPS, with an SSH key pair made for deploying. Run
   `deploy/dev/bootstrap.sh` on it as root.
   - It sets up Docker, ufw (SSH, and HTTPS from Cloudflare only), SSH with keys only and the user
     `deploy`.
   - That user's key may only run `deploy/dev/remote.sh`.
3. **Cloudflare**:
   - the domain proxied, SSL/TLS *Full (strict)*;
   - an origin certificate for the bootstrap;
   - an Access application for the domain, with the webhook paths (`/api/v1/hooks/*`,
     `/api/v1/forges/github/events`) as public overrides, so forges can still call them.
4. **GitHub**: the variable `DEV_URL` and the secrets `DEV_SSH_HOST`, `DEV_SSH_KEY`,
   `DEV_SSH_KNOWN_HOSTS` and `DEV_SEED_PASSWORD`.
5. **Run it**: Actions → *Dev instance* → *Run workflow* → *deploy*. The first deploy finds a fresh
   instance and seeds it.

From then on:
- every merge whose images are published is deployed, with the data kept, so migrations run on a
  filled database;
- at 02:00 UTC the instance is reset;
- *Run workflow* deploys or resets on demand.

By hand, the deploy key's commands are `deploy <commit>`, `reset` and `status`, with the
credentials on standard input (`deploy/dev/README.md`, *Deploying*).

The seed's users are `admin`, `viewer`, `operator` and `editor`, all with `DEV_SEED_PASSWORD`. What
each may do is in the README's table. Nothing on the instance outlives a night: the reset removes the
data, the master key and the runner's workspaces.

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
