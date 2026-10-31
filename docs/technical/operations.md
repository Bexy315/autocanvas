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
| `AUTOCANVAS_URLS` | `urls` | Addresses the server listens on, e.g. `http://+:8080` |
| `AUTOCANVAS_BOOTSTRAP_ADMIN_USERNAME` | `Bootstrap:AdminUserName` | First admin, created on start while there is no account |
| `AUTOCANVAS_BOOTSTRAP_ADMIN_PASSWORD` | `Bootstrap:AdminPassword` | That admin's initial password; must be changed at the first sign-in |
| `AUTOCANVAS_MASTER_KEY` | `Secrets:MasterKey` | The master key that encrypts secrets (see [Master key](#master-key)) |
| `AUTOCANVAS_MASTER_KEY_FILE` | `Secrets:MasterKeyFile` | A file holding the master key instead, e.g. a container secret |

Settings are checked when the server starts. An invalid value stops it with a message that names the
setting and what is expected, instead of failing later on first use.

| Setting | Default | Meaning |
|---|---|---|
| `Server:PublicUrl` | none | Absolute `http`/`https` URL users reach the server at, if it differs from what the server sees behind a reverse proxy |
| `Server:DataDirectory` | `data` | Where the server keeps its files, such as the default SQLite database; relative to the server's directory |
| `Database:Provider` | `Sqlite` | `Sqlite` or `Postgres` (see [Database](#database)) |
| `Database:ConnectionString` | SQLite file `autocanvas.db` in the data directory | Required for PostgreSQL |
| `Database:MigrateOnStart` | `true` | Bring the database schema up to date when the server starts |
| `Authentication:SessionIdleTimeout` | `7.00:00:00` | A session ends after this long without requests |
| `Authentication:SessionLifetime` | `30.00:00:00` | A session ends this long after the sign-in, however active |
| `Bootstrap:AdminUserName`, `Bootstrap:AdminPassword` | none | Create this admin on start while there is no account at all; set both or neither (see [Users and groups](../user/accounts.md#the-first-admin)) |
| `Secrets:MasterKey` or `Secrets:MasterKeyFile` | none — **required** | The master key, or a file holding it; exactly one of them (see [Master key](#master-key)) |

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

- **PostgreSQL**: `pg_dump` while the server runs.
- **SQLite**: the database runs in write-ahead-log mode, so copying `autocanvas.db` alone while the
  server runs is not safe. Stop the server and copy `autocanvas.db` with its `-wal` and `-shm`
  files, or use `sqlite3 autocanvas.db ".backup backup.db"` while it runs.

## Master key

Secrets are encrypted with a master key that is kept outside the database
([ADR-0013](../adr/0013-secrets-at-rest.md)), so a database dump or a copied SQLite file reveals no
secret. The server does not start without it, and it never creates one on its own.

1. Create a key once: `dotnet AutoCanvas.Server.dll admin generate-master-key` prints 32 random
   bytes as base64 (44 characters).
2. Give it to the server as `AUTOCANVAS_MASTER_KEY`, or put it in a file readable only by the
   server and set `AUTOCANVAS_MASTER_KEY_FILE` to its path.
3. **Back it up separately from the database.** Without it, the secrets in a backup cannot be
   recovered; with it and a database backup, anyone can read them.

On its first start the server records which key it uses (by an id derived from the key, which
reveals nothing about it). If it is later started with another key, it stops with a message naming
both key ids, instead of failing runs later.

### Rotating the key

Rotate the key when it may have leaked, or as a routine:

1. Create a new key with `admin generate-master-key`.
2. Stop the server (every instance, with PostgreSQL).
3. Run `admin rotate-master-key --new-key-stdin` (or `--new-key-file <path>`) with the **current**
   key still configured. It re-encrypts every secret under the new key in one transaction; if any
   secret cannot be decrypted with the current key, it changes nothing and names it.
4. Configure the new key and start the server. The old key no longer works; keep the new one in
   your backup.

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
| `admin rotate-master-key --new-key-stdin` or `--new-key-file <path>` | Re-encrypts every secret under a new master key; stop the server first ([Rotating the key](#rotating-the-key)) |

`admin` commands apply pending migrations first, unless `Database:MigrateOnStart` is `false`. Only
the server itself and `admin rotate-master-key` need the master key.

## Reverse proxies and HTTPS

The session cookie is marked `Secure` when the server sees an HTTPS request or when
`Server:PublicUrl` starts with `https://`. Behind a proxy that ends TLS, set `Server:PublicUrl` to
the public HTTPS address. Sign-in attempts are limited per client address; until forwarded headers
are supported (S4), all requests through one proxy count as one address.

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
