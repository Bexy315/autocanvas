# 0013. Encrypt secrets with AES-GCM under a master key

- Status: Accepted
- Date: 2026-10-30

## Context

Automations use variables (plain configuration) and secrets (tokens, passwords, keys). Both can be
defined for the whole instance, for a folder and for one automation. Secrets must be unreadable in a
database dump or backup, must never come back through the API, and must be usable by runs (S2). The
database may be a SQLite file next to the server, so the database cannot be trusted to protect them.
The origin used ASP.NET Core DataProtection with a key ring on disk that was not itself protected.

## Options

1. **ASP.NET Core DataProtection** — less own crypto code; the key ring needs its own protection
   and backup, and its format and rotation are opaque to operators.
2. **AES-256-GCM per value under a master key** — a few dozen lines on top of the base library's
   `AesGcm`; key handling is visible and documentable.
3. **External secret store** (Vault, cloud KMS) — strongest, but an extra dependency for every
   installation; a possible later addition behind the same interface.

## Decision

Option 2.

- The **master key** (256 bit) comes from `AUTOCANVAS_MASTER_KEY` or a file named by
  `AUTOCANVAS_MASTER_KEY_FILE`. The server refuses to start without it; it never generates one
  silently, because losing it makes every secret unrecoverable.
- Each value is encrypted with AES-256-GCM, a random 96-bit nonce and **associated data binding it
  to its scope and name**, so a ciphertext copied to another row fails to decrypt. The row stores
  the key id; `admin rotate-master-key` re-encrypts all values under a new key.
- **Resolution chain**: instance → folders from root to leaf → automation; the nearest definition of
  a name wins. A restricted folder ([ADR-0012](0012-folder-permissions.md)) cuts the folder part of
  the chain: someone granted only on the restricted folder must not be able to use secrets of the
  folders above it. Instance values still apply.
- An *explain* endpoint lists where each name comes from and which definition wins, never values.
- Secret values are redacted from logs by a pure `SecretRedactor` in Core (used by runs in S2).

## Consequences

- Operators must back up the master key separately from the database; the operations docs say how.
- An external secret store can be added later as another implementation of the secret port.
- Secrets reach runners only as resolved step inputs ([ADR-0008](0008-server-resolves-steps.md)).
