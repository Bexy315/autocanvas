# Deployment

How the server and the runner come as container images, how the compose package that the install scripts use is built, and how to put AutoCanvas behind a reverse proxy with HTTPS. The settings are in [Configuration](configuration.md); first steps are in [Installation](../user/installation.md).

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
- private until the first release, so pulling them needs a sign-in
  ([ADR-0023](../adr/0023-published-images-and-dev-instance.md)).

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

## Serving the web UI

The server serves the web UI on the same address as the HTTP API, from its web root (`wwwroot` next
to the server; the container image contains it). There is nothing to configure: open the server's
address in a browser. Behind a reverse proxy, forward every path to the server, not only `/api`,
and keep the headers the server sets (`Content-Security-Policy`, `Cache-Control`); see
[Web UI](web-ui.md) for what is cached how. A server built without the UI answers the UI's paths
with `404` and works otherwise as usual.
