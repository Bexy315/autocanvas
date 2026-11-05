# 0023. Publish images to GHCR and deploy a dev instance from GitHub Actions over SSH

- Status: Proposed
- Date: 2026-11-05

## Context

Until now, the server and runner images are only built: in CI, to check that they build, and by the
install scripts on the machine that runs them ([ADR-0020](0020-first-start-setup.md)). The
maintainer wants an instance for manual tests that:
- always runs the newest `main`;
- resets itself to a known state.

It runs on a small VPS behind Cloudflare. 0.1 will need published images as well.

The repository is private until 0.1 and will be public afterwards. GitHub Actions already runs its
CI. What the dev instance needs must therefore:
- work from a public repository without exposing the host;
- keep no lasting credentials on the host that would outlive their use.

## Options

**Where the images come from**
1. **GitHub Container Registry**: next to the code, with the workflow's own token for pushing.
   Private while the repository is, public with it.
2. **Docker Hub**: the usual place for public images, but another account and token to look after,
   and pull limits.
3. **Build on the host**: no registry, but a 4-core VPS builds slowly. The tested image would not be
   the one that runs.

**How the host gets a new commit**
1. **GitHub Actions over SSH**: a workflow connects after the images are pushed and runs one script.
   The deploy is visible in Actions with its log and result. It needs an SSH key in GitHub's
   secrets and port 22 open.
2. **The host polls the registry** (a timer, or a tool like Watchtower): no inbound access for
   GitHub. But it needs a lasting registry token on the host, deploys are invisible in GitHub, and
   the nightly reset needs a second mechanism.
3. **AutoCanvas deploys itself**: the best showcase, but the instance under test would deploy and
   reset itself. A broken `main` could stop the very mechanism that brings the fix.

## Decision

Images go to **GHCR**, tagged with the commit and `dev`, built by a workflow on pushes to `main`.
The dev instance is deployed and reset by **GitHub Actions over SSH**:
- **The key**: it is restricted to a forced command that accepts only *deploy*, *reset* and
  *status*.
- **Registry access**: the workflow hands the host its short-lived `GITHUB_TOKEN` for the pull, so
  the host keeps no registry credential.
- **Access to the instance**: Cloudflare Access sits in front of it, with the webhook paths
  bypassed.
- **Where the settings live**: the domain, the host and the secrets are GitHub settings, so the
  repository stays generic.

GHCR keeps images and code under one account. SSH from Actions makes every deploy and reset a
visible, logged run. Deploying the instance from outside means a broken commit can still be replaced
by the next.

## Consequences

- 0.1 adds release tags to the same images workflow instead of starting one.
- The host needs port 22 open to the internet, because GitHub's runners have no fixed addresses.
  Passwords and root login are off; the deploy key cannot open a shell.
- The deploy user can use Docker, which on that host is root in effect. This is acceptable for a
  throwaway instance with nothing on it worth keeping. It would not be for a production host.
- A self-deploy by AutoCanvas can come later, deploying to a second instance rather than to itself.
- Revisit if GitHub's runners get fixed addresses, so port 22 could be limited to them. Revisit also
  if the host grows to hold anything worth keeping.
