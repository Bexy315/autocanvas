---
sidebar_position: 2
---

# Security model

This page describes what AutoCanvas protects and how. It grows with the server; parts marked
*planned* are decided but not built yet. The decisions behind it are in the ADRs linked below.

## Assets

| Asset | Why it matters |
|---|---|
| Secrets (tokens, passwords, keys used by automations) | Give access to systems outside AutoCanvas |
| Automations | Run code on runners with those secrets |
| Accounts, sessions and API tokens | Act with someone's permissions |
| Audit log | Shows who changed what; must not be editable through the product |

## Trust boundaries

- **Browser and API clients → server**: authenticated by session cookie or API token
  ([ADR-0011](../adr/0011-local-identity.md)). Everything from a client is untrusted input.
- **Server → database**: the database is not trusted with secrets. A dump or a copied SQLite file
  reveals no secret values ([ADR-0013](../adr/0013-secrets-at-rest.md)).
- **Other systems → webhooks**: a webhook call is anonymous; the random token in the path
  (`ach_`, 32 random bytes, stored as SHA-256) is its credential, and a signed address also checks
  an HMAC-SHA256 of the body with a secret kept encrypted under the master key. Calls are rate
  limited per address and at most 1 MiB. They are exempt from the `X-AutoCanvas-Request` header,
  which guards browser sessions, because they never carry one.
- **Server → runners**: runners authenticate with their own credentials on a port of their own
  ([ADR-0017](../adr/0017-runner-endpoint-and-credentials.md)) and receive resolved jobs and steps,
  one step at a time, including only the secrets those steps use
  ([ADR-0008](../adr/0008-server-resolves-steps.md)). An offered job's environment contains no
  secrets; service containers get the secrets their environment names.
- **Runner → jobs**: the reference runner holds the Docker socket and is trusted with its host; the
  jobs it runs are not. A job's container gets its workspace and the runner's files for that job
  (scripts, output files), never the Docker socket, and runs with `no-new-privileges` and a process
  limit. `docker_build` runs rootless BuildKit in a container of its own with the default seccomp
  and AppArmor profiles lifted (BuildKit makes namespaces of its own); it is not privileged, holds
  no capability on the host and never the Docker socket, and registry credentials are handed to it
  for one build only. Checkout credentials reach git as an HTTP header, never in `.git/config`.
  A step's secrets reach only that step's process, as environment variables, and the runner
  masks them in everything it sends back before the server masks them again. The runner's
  credential is kept in a file readable by its user only and never logged.

## Authentication

Local accounts ([ADR-0011](../adr/0011-local-identity.md)). Passwords are hashed with
PBKDF2-HMAC-SHA256, 600,000 iterations and a random 16-byte salt per password, and compared in
constant time. The stored hash names its algorithm and cost, so the cost can rise later and old
hashes are replaced at the next sign-in. Passwords have 12 to 256 characters and must not equal the
user name. A password set by someone else (an admin, the command line, the bootstrap settings) must
be changed at the next sign-in, and resetting a password ends the user's sessions. At least one
enabled admin always exists. The command line can reset a password for recovery; it needs the
server's database settings, so only operators can use it.

**A fresh installation** has no account. Until it is set up, the server answers only its setup
API ([ADR-0020](../adr/0020-first-start-setup.md)), which needs a one-time token of 32 random bytes
that the server keeps in memory and writes only to its log. The token is compared in constant time,
one address may make 20 setup requests a minute, and the setup creates the first admin and its
settings in one transaction, after which the setup API is gone. Whoever can read the server's log can
set it up, so finish the setup right after the first start, before the server is reachable from
untrusted networks.

Sign-in failures look the same and take the same time whether the user name is unknown, the
password wrong or the account disabled (an unknown name is checked against a dummy hash). Five
failures for a name within 15 minutes lock it for 15 minutes, and one network address may try ten
times a minute. Both limits are kept in memory per server instance, so with several instances an
attacker gets the attempts once per instance. Every failure is in the audit log.

Sessions are opaque random
tokens (32 random bytes) in `HttpOnly`, `SameSite=Strict` cookies, `Secure` whenever the server is
reached over HTTPS; the database stores only their SHA-256 hash. A session ends after 7 days
without requests and 30 days after the sign-in at the latest (configurable), and on sign-out,
"sign out everywhere", a password change (all other sessions) and when the user is disabled.
Users can list their sessions and end any of them.

Requests that change something must send `X-AutoCanvas-Request: 1` unless they carry an API token:
a foreign page cannot set that header without a CORS preflight, and the server allows no
cross-origin requests, so a signed-in browser cannot be made to act by another website (this
includes signing it in to an attacker's account).

The web UI is served by the server on the API's own origin ([ADR-0018](../adr/0018-web-ui.md)),
so it uses the same session cookie and header and needs no cross-origin access. Its page carries a
content security policy that allows scripts, connections and other resources only from that origin
and forbids framing (clickjacking); every response is sent with `X-Content-Type-Options: nosniff`
and `Referrer-Policy: same-origin`. The policy allows inline styles, which React and the canvas
use for positioning, but no inline scripts. Details in [Web UI](web-ui.md).

API tokens are `acp_` followed by 32 random bytes, shown once at creation and stored only as a
SHA-256 hash with a short hint for recognition. They can expire, act with the owner's current
permissions (a disabled owner disables them), and are subject to the same password-change rule as
sessions. A token cannot create or list tokens, list sessions or change the password, so a leaked
token cannot lock its owner out or create more tokens. Admins can list and delete any user's tokens.
The `acp_` prefix lets secret scanners recognize leaked tokens.

Runners register with a one-time registration token (`acrt_`, created by an admin, valid for 24
hours by default and at most 30 days) and receive a runner credential (`acr_`). Both are 32 random
bytes, shown once and stored only as SHA-256 hashes; a token registers one runner, also when two
registrations race. Runner calls are checked by their own interceptor, which accepts only runner
credentials: user sessions and API tokens never authenticate a runner, and runner credentials never
authenticate the HTTP API. Deleting a runner revokes its credential, and a connected runner is told
not to reconnect.

## Authorization

Roles (Viewer, Operator, Editor, Owner) are granted on folders to users and groups, inherited
down the tree and cut by restricted folders; instance admins have every role
([ADR-0012](../adr/0012-folder-permissions.md), user guide *Folders and permissions*). The rule is
one pure function; the use cases evaluate it for every request against the folder chain and the
caller's own and group grants, so nothing is cached that could go stale. Folders a caller may not
view answer *not found*, like folders that do not exist, and lists contain only what the caller
may view. Above a visible folder, callers see only the names of the folders, for orientation.
Whoever restricts or moves a folder keeps their own role through an explicit grant, so nobody
locks themselves out by accident; admins can repair any other lock-out.

## Secrets

Secret values are encrypted with AES-256-GCM, one random nonce per value, under a master key held
outside the database ([ADR-0013](../adr/0013-secrets-at-rest.md)). The associated data binds each
ciphertext to its scope and name, so a value copied to another row or renamed does not decrypt. The
server refuses to start with a different key than the database was set up with. Without a configured
key, a fresh installation generates one into its data directory (mode 600), and the setup shows it
once to be stored apart from the database; a database that already holds a key id never gets a
generated key ([ADR-0020](../adr/0020-first-start-setup.md)).
No use case returns a secret value, not even to admins, and the audit log records only names; the
values are decrypted only to resolve them for runs. A restricted folder also cuts the inheritance
of variables and secrets from the folders above it. `SecretRedactor` in Core replaces known secret
values in text before it is stored (used for run logs in S2). The key is rotated offline with
`admin rotate-master-key` ([Operations](operations.md#master-key)).

## Audit

Every change writes an audit event in the same transaction as the change: who, what, which object,
when and from where. Events never contain secret values, and AutoCanvas offers no way to change or
delete them; only the configured retention removes old ones (`audit.pruned` records that). The
log covers schema migrations, accounts, tokens, groups, folders and grants, automations, variables
and secrets. Admins read all of it; folder owners read the entries of the folders they own. Fields,
queries and the list of actions are in [Audit log](audit.md).

## Reporting a vulnerability

AutoCanvas is pre-alpha and not yet public. A disclosure process will be published with the first
release.
