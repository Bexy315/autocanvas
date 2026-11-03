# 0020. Set up a fresh instance in the browser, with a token from the log

- Status: Accepted
- Date: 2026-11-03 (accepted 2026-11-04)

## Context

Getting a running instance takes, as of S3, several manual steps:
- create a master key with a command and pass it in ([ADR-0013](0013-secrets-at-rest.md));
- create the first admin with `admin create` or the `AUTOCANVAS_BOOTSTRAP_ADMIN_*` variables
  ([ADR-0011](0011-local-identity.md));
- set `Server:PublicUrl` behind a proxy;
- create a registration token in the UI and register a runner with it.

S4 aims at one short command and a few questions in the browser. Several things stand in the way of
that:
- **No account**: a fresh server has none, so nobody may sign in to answer those questions.
- **Exposed until set up**: the server may be reachable from a network before its owner looks at it.
- **No key**: the master key must exist before the first secret is stored.
- **Settings in configuration only**: settings such as the public address live only in
  configuration, which the UI cannot change.

## Options

1. **Keep setup in configuration and commands, documented step by step.** Nothing new to secure,
   but it is the multi-step path S4 wants to replace.
2. **An open first-run page**: whoever reaches a fresh server first creates the admin. This is
   simple, and common in self-hosted tools, but anyone on the network who is quicker owns the
   instance.
3. **A setup mode guarded by a one-time token printed to the server's log**: only someone who can
   read the log (the person who started it) can finish the setup. The UI asks the questions;
   configuration still wins where it is set.

## Decision

Option 3.

- **Setup mode**: a server starts in setup mode when its database has no account and no
  `Bootstrap:*` admin is configured.
  - It creates a random setup token (kept in memory, so a restart creates a new one) and logs the
    address `/setup?token=…`.
  - Until setup is finished it serves health checks, the web UI and `/api/v1/setup` only. Every
    other API request is answered with `503` and a problem saying that setup is required.
  - Finishing setup creates the first admin and the chosen settings in one transaction and ends
    setup mode for good. The token is compared in constant time, and attempts are rate-limited.
- **The other ways stay**: `admin create` and the bootstrap variables still create the first admin
  and skip the wizard, for scripted installations.
- **A generated master key**: when no key is configured and the database has recorded no key id,
  the server generates a key into `Secrets:MasterKeyFile`, by default `keys/master.key` in its data
  directory, readable by the server's user only.
  - The setup shows the key once and asks for it to be stored apart from the database. The compose
    package puts the key directory on a volume of its own.
  - Once the database has a key id, a missing key stops the server as before.
  - This narrows ADR-0013's rule that the server never generates a key to installations that have
    stored no secret yet.
- **Instance settings in the database**: the public address and the address runners use are kept
  in the database, so the setup and **Administration** can change them.
  - A value set in configuration wins and is shown as such.
  - Settings that the server needs before it can read its database (database, listening addresses,
    master key) stay in configuration only.
- **The bundled runner**: the setup can hand a registration token to the runner of the compose
  package through a file on a shared volume. A runner configured with `Runner:RegistrationTokenFile`
  waits for that file when it has no credential, registers with it and runs. Any other runner
  registers as before.

## Consequences

- A fresh, exposed server is safe until set up only as far as its log is private. The docs say so
  and recommend finishing setup right after the start.
- A generated key lives next to the data unless the operator moves it. The compose package and the
  operations docs separate them, and the setup asks for a backup.
- Settings now come from two places: configuration and database. The rule "configuration wins,
  shown as locked" must hold everywhere such a setting is read.
- The runner gains a way to register without a person running `register`. A token file left on the
  volume is harmless once used, since registration tokens are single-use.
- Revisit this if the instance gains other sign-in methods (OIDC) that could create the first admin
  themselves.
