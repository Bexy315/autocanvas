# 0022. Integrate GitHub through one GitHub App per instance

- Status: Accepted
- Date: 2026-11-04

## Context

S5 connects GitHub repositories ([ADR-0021](0021-repositories-per-folder.md)). For that, AutoCanvas
must:
- read files and check out code, including private repositories;
- receive push and pull request events;
- report commit statuses;
- comment on pull requests;
- push a branch and open a pull request when someone edits a repository automation.

GitHub offers personal access tokens, OAuth apps and GitHub Apps for this.

## Options

1. **Personal access tokens per repository**: simple to build, but the tokens are long-lived, act as
   a person, and every repository needs its webhook set up by hand.
2. **An OAuth app**: acts as the signed-in user; it suits "sign in with GitHub" more than an
   integration, and still needs webhooks per repository.
3. **One GitHub App per instance**:
   - created from a manifest in one step;
   - installed on accounts or organizations, with the repositories chosen there;
   - short-lived installation tokens;
   - one webhook for all events;
   - statuses, checks and comments show as the app.

## Decision

Option 3.

- **Creating the app**: an admin creates it under **Administration → Forges** with GitHub's
  manifest flow. AutoCanvas sends the manifest:
  - name and the instance's public address;
  - webhook address `/api/v1/forges/github/events`;
  - permissions: contents read and write, pull requests read and write, statuses read and write,
    checks read and write, metadata read;
  - events: push, pull request, installation.

  GitHub returns the app's id, its private key, its webhook secret and its client secret, which
  AutoCanvas stores as secrets under the master key ([ADR-0013](0013-secrets-at-rest.md)). An
  existing app can be entered by hand instead.
- **Installations**: the app is installed on accounts or organizations in GitHub. In a folder,
  editors connect a repository by choosing it from the installations AutoCanvas sees.
- **Tokens**: the server signs a short JWT with the app's key and exchanges it for an installation
  token (valid one hour, cached until shortly before it expires). The token is used for the API, for
  discovery and for checkouts, where it reaches the runner like any checkout credential, only for
  that job.
- **Events**: one webhook for the app, verified with HMAC-SHA256 (`X-Hub-Signature-256`), then
  deduplicated by delivery id.
- **One app per instance.** An instance that needs several GitHub hosts or accounts with separate
  apps is a later extension.

## Consequences

- An admin needs rights to create an app in a GitHub account or organization, and the instance's
  public address must be reachable from GitHub for events. Without that, repositories still work
  through polling.
- The app's private key is the most valuable secret of the instance after the master key; rotating
  it is done in GitHub and then entered in AutoCanvas.
- Pull requests from forks get events like any other; whether their runs see secrets is decided in
  the plan, defaulting to no.
- GitHub Enterprise Server works the same with another base address, but is not tested in S5.
