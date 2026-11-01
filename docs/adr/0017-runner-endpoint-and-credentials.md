# 0017. Serve runners on their own HTTP/2 port, with their own credentials

- Status: Accepted
- Date: 2026-10-31 (accepted 2026-11-01)

## Context

The runner protocol is gRPC over HTTP/2 ([ADR-0004](0004-runner-protocol-grpc-spec-first.md)).
The server's HTTP API is reached by browsers with HTTP/1.1. Kestrel negotiates HTTP/2 only with TLS
(ALPN): an endpoint that serves both protocols without TLS speaks HTTP/1.1 only, so an unencrypted
runner on a local network could not connect. Runners must authenticate on every call
(runner protocol v1), and nothing a user signs in with should work for a runner — nor the other
way round. A runner's control stream lasts for hours and may move to another server after a
reconnect when several servers share PostgreSQL.

## Options

For the endpoint:

1. **The HTTP API's port** — one port to open, but gRPC needs TLS (or a proxy speaking HTTP/2) all
   the way, also on a local network.
2. **A port of its own, HTTP/2 only** — works without TLS; operators open and proxy a second port.
3. **Both, configurable** — the most flexible, and twice the documentation and tests.

For runner authentication:

1. **The HTTP API's authentication scheme** with a runner claim — one handler, but one mistake in a
   policy lets a user token reach the runner services or a runner credential the API.
2. **A gRPC interceptor** that checks only runner credentials — the HTTP API's handler ignores gRPC
   calls, and the interceptor never looks at sessions or API tokens.

## Decision

- Runners connect to **a port of their own that speaks only HTTP/2**: `Runners:Urls`, by default
  `http://localhost:5081`. The server turns `urls` and `Runners:Urls` into Kestrel endpoints; whoever
  configures `Kestrel:Endpoints` directly configures both. Behind a reverse proxy the runner port
  needs a proxy that forwards gRPC (HTTP/2 with trailers).
- A runner registers with a **one-time registration token** (`acrt_…`, by default valid for 24 hours,
  at most 30 days) created by an admin, and gets a **runner credential** (`acr_…`). Both are 32 random
  bytes, shown once and stored as SHA-256 hashes like API tokens
  ([ADR-0011](0011-local-identity.md)); marking a token used is guarded by a concurrency token, so
  it registers one runner only.
- `RunnerService` calls are authenticated by **a gRPC interceptor** that accepts only runner
  credentials and sets the call's user to the runner, which the audit log records as actor kind
  `Runner`. The HTTP API's handler skips gRPC calls, and its token check refuses anything without the
  `acp_` prefix, so neither side accepts the other's credentials. Deleting a runner revokes its
  credential.
- Each control stream is served by a **session** that keeps nothing the database does not have:
  leases, holders and pause state are stored, so a runner can reconnect to any server. A session
  offers jobs while the runner is neither paused nor draining and holds fewer jobs than it runs at
  once, and wakes on the queue's signal, on messages that free room, or once per heartbeat interval.
  When the server stops, every runner is told to reconnect after five seconds, and the server waits
  until the sessions have ended.

## Consequences

- A local runner needs no certificate; production setups put TLS on the runner port as on the API
  port, or terminate it in a proxy that speaks HTTP/2 to the server.
- Two ports to open and document. A later single-port mode (TLS only) can be added without changing
  the protocol.
- A user token never authenticates a runner, so scripts cannot impersonate one, and a stolen runner
  credential cannot read the API.
- Pausing, deleting and reconnecting work across several servers because a session reads them from
  the database; their effect shows on the next look, at most one heartbeat interval later.
