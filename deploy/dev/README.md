# A throwaway dev instance

The files here run an AutoCanvas instance for manual tests:
- it always runs the newest `main`;
- it resets itself every night, and on demand, to the state described below;
- it is the [compose package](../compose/) with PostgreSQL, from the images CI publishes.

The plan is [D1](../../docs/plans/d1.md); [ADR-0023](../../docs/adr/0023-published-images-and-dev-instance.md)
says why it is built this way. GitHub Actions deploys and resets it ([GitHub Actions](#github-actions)).

| File | What it is |
|---|---|
| `seed.sh` | Fills a fresh instance with test users, folders and runs ([The seed](#the-seed)) |
| `bootstrap.sh` | Turns a fresh Ubuntu 24.04 host into the instance's host ([The host](#the-host)) |
| `remote.sh` | The one command the deploy key may run: `deploy`, `reset`, `status` ([Deploying](#deploying)) |
| `compose.dev.yaml` | Added to the compose package: Caddy in front, the server's port only on the host |
| `Caddyfile` | TLS with Cloudflare's origin certificate, the visitor's address passed on |

## The seed

`seed.sh` fills a **fresh** instance through the public API, as a person in the web UI would. On an
instance that is set up already it says so and changes nothing.

```sh
AUTOCANVAS_SEED_PASSWORD='…' deploy/dev/seed.sh --url http://localhost:5080 --token <setup token> \
  --public-address https://dev.example.org
```

- The setup token is in the server's log (`docker compose logs server`).
- `--public-address` is optional.
- All users get the seed password (at least 12 characters). It needs bash, `curl` and `jq`.

| What | Seeded |
|---|---|
| Users | `admin` (administrator), `viewer` (Vera Viewer), `operator` (Otto Operator), `editor` (Edith Editor). Each has changed their initial password already, so they sign in with the seed password. |
| Group | `testers`: viewer, operator and editor |
| Folders | **Getting started** (the setup's example) · **Team**, with **Team / Builds** and **Team / Restricted** |
| Rights | On Team: `testers` Viewer, `operator` Operator, `editor` Editor. Restricted takes no rights from above; only `editor` has Editor there. |
| Configuration | On Team: the variable `GREETING` and the secret `DEPLOY_TOKEN` (a new random value on every reset) |
| Automations | `hello` (the setup's example) · `build-and-test` in Builds: two jobs, a JUnit report with 5 tests (1 skipped) and coverage, and a schedule every 6 hours · `uses-secret`: prints the variable and uses the secret, which the log masks · `fails-on-purpose`: always fails · `deploy` in Restricted |
| Runs | A first run of each automation but `deploy`. `fails-on-purpose`'s fails; the others succeed. The seed fails if any ends otherwise. |
| Runner | The compose package's bundled runner (`linux`, `docker`) |

So, for example:
- `viewer` sees Team and Builds but cannot start runs;
- `operator` can start them;
- `editor` can change the automations and sees Restricted;
- `admin` sees everything.

CI runs the seed against the compose package with PostgreSQL (`deploy/test/compose-smoke.sh --seed`). It checks what each user sees, and that a second seed changes nothing.

## The host

Run `bootstrap.sh` once as root on a fresh Ubuntu 24.04 host, from a copy of the repository's
`deploy` directory:

```sh
deploy/dev/bootstrap.sh --domain dev.example.org --deploy-key deploy.pub \
  --cert origin-cert.pem --key origin-key.pem [--images ghcr.io/<owner>]
```

- **Domain**: proxied by Cloudflare, with SSL/TLS set to *Full (strict)*.
- **Certificate**: an origin certificate from Cloudflare (SSL/TLS → Origin Server) and its key.
- **Deploy key**: the public key of a new SSH key pair for deploying; its private key goes to GitHub
  ([GitHub Actions](#github-actions)).

Running it again is harmless and refreshes the scripts, the compose files and Cloudflare's address
ranges. It starts nothing; the first deploy does.

What it sets up:
- **Updates**: security updates every day (unattended-upgrades, no automatic reboots), and
  fail2ban for SSH.
- **Docker CE** from Docker's repository, with logs that rotate themselves.
- **SSH with keys only**: no passwords; root only with a key; only `root` and `deploy` may sign in.
- **The user `deploy`** (in the `docker` group, no password). Its `authorized_keys` is owned by root
  and allows its key exactly one thing: `restrict,command="/opt/autocanvas-dev/remote.sh"`.
- **The instance** in `/opt/autocanvas-dev/instance`:
  - the compose package with PostgreSQL and `compose.dev.yaml`;
  - its `.env`, written once (domain, image repository, a random database password);
  - the origin certificate in `/etc/autocanvas-dev/origin`, readable by root only.
  - `remote.sh` lies beside it, owned by root, so the deploy key cannot change what it runs.
- **Firewall** (ufw): SSH (22) from anywhere, because GitHub's runners have no fixed addresses;
  HTTPS (443) from Cloudflare's addresses only; nothing else.
  - Docker's published ports would bypass ufw, so the instance publishes the server only on
    `127.0.0.1`.
  - Caddy runs in the host's network, where ufw applies.
  - The runner port is not published: the bundled runner reaches the server inside the compose
    network.

## Deploying

The deploy key runs `remote.sh` and nothing else; what to do is the SSH command:

| Command | What it does |
|---|---|
| `deploy <commit>` | Pulls `sha-<commit>` of both images and starts them; migrations run as the server starts. Then it checks that the server names the commit, seeds the instance if it is fresh, and keeps the newest three commits' images. |
| `reset` | `docker compose down --volumes` (data, master key, runner workspaces), a fresh start and the seed. |
| `status` | The containers and the running version. |

Credentials come on standard input as header lines, never as arguments, so they show in no process
list or log:
- `REGISTRY_TOKEN=` for pulling, used for this run only;
- `SEED_PASSWORD=`.

An empty line ends the headers. A `tar.gz` of the commit's `deploy/compose` and `deploy/dev` may
follow; the instance then takes that commit's compose files, Caddyfile and seed.

By hand, for example:

```sh
{ printf 'REGISTRY_TOKEN=%s\nSEED_PASSWORD=%s\n\n' "$token" "$password"; tar -czf - -C deploy compose dev; } |
  ssh -i deploy-key deploy@<host> deploy "$(git rev-parse HEAD)"
```

Deploys and resets wait for each other. Changes to `remote.sh` or `bootstrap.sh` reach the host only
by running `bootstrap.sh` again.

## GitHub Actions

`.github/workflows/dev.yml` does it from GitHub:

| When | What |
|---|---|
| After the *Images* workflow published `main`'s images | `deploy <that commit>`, with that commit's compose files and seed |
| Every night at 02:00 UTC | `reset` |
| By hand (Actions → *Dev instance* → *Run workflow*) | `deploy` the newest commit on `main` with published images, or `reset` |

One run at a time; none is cancelled. The pull uses the workflow's own short-lived token. Each
run's summary names the commit and shows `status`; a failed run fails the workflow, and GitHub
mails about it.

It needs these repository settings:

| Name | Kind | Value |
|---|---|---|
| `DEV_URL` | variable | The instance's address, such as `https://dev.example.org`. Without it the workflow does nothing, so forks without a dev instance are left alone. |
| `DEV_SSH_HOST` | secret | The host's address |
| `DEV_SSH_KEY` | secret | The deploy key's private key |
| `DEV_SSH_KNOWN_HOSTS` | secret | The host's keys (`ssh-keyscan <host>`); compare their fingerprints with `ssh-keygen -lf /etc/ssh/ssh_host_*_key.pub` on the host first |
| `DEV_SEED_PASSWORD` | secret | The seeded users' password, at least 12 characters |
