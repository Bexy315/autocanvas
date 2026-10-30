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

*Planned, S1 step 4b/4c:* login attempts are rate-limited, and failures take the same time whether
or not the account exists. Sessions are opaque random
tokens in `HttpOnly`, `Secure`, `SameSite=Strict` cookies, stored only as hashes, with sliding and
absolute expiry, and revocable. Cookie-authenticated state changes need an antiforgery header. API
tokens are shown once, stored as hashes, and can expire.

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
