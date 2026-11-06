# Configuration

Every setting of the server, where it is read from, and the two addresses an admin sets in the web UI. For running and looking after a server, see [Operations](operations.md); for putting it into containers or behind a proxy, see [Deployment](deployment.md).

## Where settings come from

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
| `AUTOCANVAS_RUNNER_URLS` | `Runners:Urls` | Addresses runners connect to, e.g. `http://+:5081` (see [Runners](operations.md#runners)) |
| `AUTOCANVAS_BOOTSTRAP_ADMIN_USERNAME` | `Bootstrap:AdminUserName` | First admin, created on start while there is no account |
| `AUTOCANVAS_BOOTSTRAP_ADMIN_PASSWORD` | `Bootstrap:AdminPassword` | That admin's initial password; must be changed at the first sign-in |
| `AUTOCANVAS_MASTER_KEY` | `Secrets:MasterKey` | The master key that encrypts secrets (see [Master key](operations.md#master-key)) |
| `AUTOCANVAS_MASTER_KEY_FILE` | `Secrets:MasterKeyFile` | A file holding the master key instead, e.g. a container secret |
| `AUTOCANVAS_TRUSTED_PROXIES` | `Server:TrustedProxies` | Reverse proxies whose forwarded headers are believed (see [Reverse proxies and HTTPS](deployment.md#reverse-proxies-and-https)) |
| `AUTOCANVAS_SETUP_RUNNER_TOKEN_FILE` | `Setup:RunnerTokenFile` | Where the setup hands a registration token to the bundled runner (see [Setting up a fresh installation](operations.md#setting-up-a-fresh-installation)) |

Settings are checked when the server starts. An invalid value stops it with a message that names the
setting and what is expected, instead of failing later on first use.

| Setting | Default | Meaning |
|---|---|---|
| `Server:PublicUrl` | none | Absolute `http`/`https` URL users reach the server at, if it differs from what the server sees behind a reverse proxy. Set here, it wins over the address set in the web UI (see [Instance settings](#instance-settings)) |
| `Runners:PublicUrl` | none | Absolute `http`/`https` URL runners reach the runner endpoint at, shown in the commands to register one; wins over the web UI's |
| `Setup:Enabled` | `true` | Whether a server without accounts opens the setup; `false` leaves the first admin to `admin create` and the bootstrap settings |
| `Setup:RunnerTokenFile` | none | A file the setup writes a registration token to, for a runner that reads it from a shared volume |
| `Repositories:CacheDirectory` | `repositories` in the data directory | Where the server keeps a shallow copy of each connected repository (see [Repositories and forges](operations.md#repositories-and-forges)) |
| `Repositories:GitTimeout` | `00:01:00` | How long one git call of the server may take, 5 seconds to 10 minutes |
| `Server:TrustedProxies` | none | Reverse proxies whose `X-Forwarded-For` and `X-Forwarded-Proto` are believed: addresses or networks (`10.0.0.0/8`), separated by commas (see [Reverse proxies and HTTPS](deployment.md#reverse-proxies-and-https)) |
| `Server:DataDirectory` | `data` | Where the server keeps its files, such as the default SQLite database; relative to the server's directory |
| `Database:Provider` | `Sqlite` | `Sqlite` or `Postgres` (see [Database](operations.md#database)) |
| `Database:ConnectionString` | SQLite file `autocanvas.db` in the data directory | Required for PostgreSQL |
| `Database:MigrateOnStart` | `true` | Bring the database schema up to date when the server starts |
| `Authentication:SessionIdleTimeout` | `7.00:00:00` | A session ends after this long without requests |
| `Authentication:SessionLifetime` | `30.00:00:00` | A session ends this long after the sign-in, however active |
| `Bootstrap:AdminUserName`, `Bootstrap:AdminPassword` | none | Create this admin on start while there is no account at all; set both or neither (see [Users and groups](../user/accounts.md#the-first-admin)) |
| `Runners:Urls` | `http://localhost:5081` | Addresses of the runner endpoint, HTTP/2 only; separate several with `;` (see [Runners](operations.md#runners)) |
| `Runners:HeartbeatInterval` | `00:00:20` | How often runners send a heartbeat; at least one second and at most half of `Queue:LeaseDuration` |
| `Logs:MaxBytesPerJob` | `67108864` (64 MiB) | Log text one job attempt may store, 1 MiB to 1 GiB; later lines are dropped (see [Runs](../user/runs.md#logs)) |
| `Reports:MaxBytesPerFile` | `52428800` (50 MiB) | Largest report file a runner may upload, 1 MiB to 1 GiB (see [Reports](../user/reports.md#limits)) |
| `Reports:MaxFilesPerJob` | `100` | Report files one job attempt may upload, 1 to 1000 |
| `Webhooks:PerMinute` | `30` | Calls one webhook address takes per minute, 1 to 10,000; more are answered with 429 (see [Triggers](../user/triggers.md#webhooks)) |
| `Runs:RetentionDays` | `90` | Delete finished runs older than this many days, with their logs and reports, once a day; `0` keeps them forever (see [Runs](../user/runs.md#how-long-runs-are-kept)) |
| `Runs:KeepPerAutomation` | `20` | The newest runs of each automation, kept however old they are; 0 to 1000 |
| `Queue:LeaseDuration` | `00:01:00` | How long a runner holds a job after its last heartbeat (10 seconds to 1 hour); then an offered job goes back to the queue and a running one fails as if the runner were gone |
| `Audit:RetentionDays` | `365` | Delete audit entries older than this many days, once a day; `0` keeps them forever (see [Audit log](audit.md#retention)) |
| `Secrets:MasterKey` or `Secrets:MasterKeyFile` | `keys/master.key` in the data directory, generated on the first start | The master key, or a file holding it; at most one of them (see [Master key](operations.md#master-key)) |

## Instance settings

The public address and the runner address can be set in the web UI (**Administration** →
**Instance**) and through `GET`/`PUT /api/v1/instance`; they are kept in the database. When `Server:PublicUrl` or
`Runners:PublicUrl` is set in the configuration, that value wins and the web UI shows it as locked.
The public address decides, with the request, whether the session cookie is marked `Secure`.
