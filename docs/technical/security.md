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
- **Server → runners** (*planned, S2*): runners authenticate with their own credentials and receive
  resolved steps one at a time, including only the secrets those steps use
  ([ADR-0008](../adr/0008-server-resolves-steps.md)).

## Authentication

Local accounts ([ADR-0011](../adr/0011-local-identity.md)). Passwords are hashed with
PBKDF2-HMAC-SHA256, 600,000 iterations and a random 16-byte salt per password, and compared in
constant time. The stored hash names its algorithm and cost, so the cost can rise later and old
hashes are replaced at the next sign-in. Passwords have 12 to 256 characters and must not equal the
user name. A password set by someone else (an admin, the command line, the bootstrap settings) must
be changed at the next sign-in, and resetting a password ends the user's sessions. At least one
enabled admin always exists. The command line can reset a password for recovery; it needs the
server's database settings, so only operators can use it.

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

API tokens are `acp_` followed by 32 random bytes, shown once at creation and stored only as a
SHA-256 hash with a short hint for recognition. They can expire, act with the owner's current
permissions (a disabled owner disables them), and are subject to the same password-change rule as
sessions. A token cannot create or list tokens, list sessions or change the password, so a leaked
token cannot lock its owner out or create more tokens. Admins can list and delete any user's tokens.
The `acp_` prefix lets secret scanners recognize leaked tokens.

## Authorization

*Planned, S1.* Roles granted on folders to users and groups, inherited down the tree, cut by
restricted folders; instance admins see everything ([ADR-0012](../adr/0012-folder-permissions.md)).
Every endpoint declares the role it needs; lists only contain what the caller may see.

## Secrets

*Planned, S1.* AES-256-GCM per value under a master key held outside the database, bound to scope
and name; write-only through the API; redacted from logs; master key rotation by command
([ADR-0013](../adr/0013-secrets-at-rest.md)).

## Audit

Every change writes an audit event in the same transaction as the change: who, what, which object,
when and from where. Events never contain secret values, and AutoCanvas offers no way to change or
delete them. Today the log records schema migrations; accounts, groups, grants, folders,
automations, variables and secrets follow with their S1 steps. Fields and the list of actions are
in [Audit log](audit.md).

## Reporting a vulnerability

AutoCanvas is pre-alpha and not yet public. A disclosure process will be published with the first
release.
