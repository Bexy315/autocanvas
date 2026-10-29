# 0004. Runner protocol: gRPC, specification first, versioned

- Status: Accepted
- Date: 2026-10-29

## Context

Runners register with the server, receive jobs, stream logs, report step status and outputs,
upload reports and artifacts, and react to cancellation. The origin engine used ASP.NET Core
SignalR for this, which is convenient in .NET but awkward to implement elsewhere. AutoCanvas wants
runners in other languages later (a Rust runner is a likely candidate) and treats log throughput
and dispatch latency as performance concerns.

## Options

1. **SignalR** — keep what the origin has. Clients outside .NET are poorly supported.
2. **HTTP + WebSocket with JSON**, described by OpenAPI/AsyncAPI — easy to debug and proxy, but
   streaming semantics, back-pressure and typing are hand-made.
3. **gRPC** with Protocol Buffers — typed contracts, bidirectional streaming, first-class libraries
   for .NET, Rust, Go and others; needs HTTP/2 end to end.

## Decision

Option 3, and the protocol is a **specification first**: the `.proto` files in `proto/autocanvas/runner/v1/`
plus a prose document are the contract; the .NET runner is a reference implementation of it.

- Control traffic (registration, heartbeat, job offers, leases, cancellation, status events, log
  chunks) runs over a long-lived bidirectional stream.
- Large payloads (artifacts, reports) are uploaded separately in chunks, not over the control
  stream.
- Within `v1`, changes are additive only; `buf lint` and `buf breaking` enforce this in CI.

## Consequences

- A second runner implementation needs only the `.proto` files and the spec.
- Reverse proxies in front of the server must support HTTP/2 (gRPC) — documented in the setup
  guide. If this proves to be a real obstacle, gRPC-Web or a JSON transcoding fallback can be
  added without changing the contract.
- Protocol changes are deliberate: every change touches the spec, the generated code and the
  reference runner together.
