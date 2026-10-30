# 0011. Own local identity: users, sessions and API tokens

- Status: Proposed
- Date: 2026-10-30

## Context

AutoCanvas must work without an external identity provider: a fresh installation needs users who
can log in to the web UI and tools (CLI, scripts, CI jobs) that call the API. OIDC login is planned
for later and must fit next to local accounts. The origin engine delegated all of this to an
external OIDC provider and had no user API tokens.

## Options

1. **ASP.NET Core Identity** — complete framework with user manager, lockout and two-factor
   scaffolding. Brings a fixed schema and many features we do not need, and is shaped around
   Razor UI flows.
2. **Own lean user store** — users and groups tables, a password hasher, database-backed sessions
   and API tokens, built on ASP.NET Core authentication handlers.
3. **Stateless JWTs for the UI** — no session table, but no real logout or revocation without a
   deny list.

## Decision

Option 2.

- **Passwords**: Argon2id if a maintained package passes the license gate, otherwise
  PBKDF2-HMAC-SHA256 with 600,000 iterations from the base library. The stored hash names its
  algorithm and parameters, so hashes are upgraded on the next successful login. Failed logins take
  the same time whether or not the user exists; login attempts are rate-limited per client address
  and per account.
- **Sessions**: an opaque random token in an `HttpOnly`, `Secure`, `SameSite=Strict` cookie. The
  database stores only its SHA-256 hash, with sliding and absolute expiry. Sessions end on logout,
  on "sign out everywhere", and when an admin disables the user. State-changing requests that
  authenticate by cookie must also send an antiforgery header.
- **API tokens**: `acp_` followed by 32 random bytes, shown once, stored as a SHA-256 hash with a
  short hint for recognition; optional expiry; sent as a bearer token. A token acts with its owner's
  current permissions. Scoped tokens are a later addition.
- **First admin**: created by the server's `admin create` subcommand, or from
  `AUTOCANVAS_BOOTSTRAP_ADMIN_*` variables when the user table is empty. The setup wizard (S4)
  replaces neither.

## Consequences

- We own security-sensitive code; it gets focused tests (expiry, revocation, timing-independent
  failure path, rate limits) and a section in the security model.
- OIDC can be added as another way to create a session for a (linked) local user, without changing
  permissions or tokens.
- Two-factor authentication and password reset by e-mail are not part of S1.
