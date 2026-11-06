# A dev instance of your own

A throwaway instance for manual tests. This is how the maintainers test AutoCanvas by hand, and the
same works for a fork:
- it runs the newest `main` of your repository or fork;
- it resets itself every night to a seed of test users, folders and runs;
- it is reachable through Cloudflare Access only.

[ADR-0023](../adr/0023-published-images-and-dev-instance.md) explains the choices. Everything is in
the repository's `deploy/dev` (its README describes each file) and `.github/workflows/dev.yml`;
nothing in them names a domain or a host.

To set one up:
1. **Images**: the *Images* workflow publishes `main` to GHCR under the repository's owner
   ([Container image](deployment.md#container-image)).
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
