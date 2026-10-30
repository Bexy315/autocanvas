---
sidebar_position: 4
---

# API conventions

The AutoCanvas server offers an HTTP API that the web UI, the command line and scripts use alike.
This page describes the rules every endpoint follows. The endpoints themselves are described by the
OpenAPI document the server serves at `/openapi/v1.json`; the same document is kept in the
repository as `openapi/v1.json`, and a test fails when the two differ.

## Versions

All endpoints live under `/api/v1`. Within `v1`, changes are additive: new endpoints, new optional
fields in requests, new fields in responses. Clients must ignore fields they do not know. A change
that breaks clients gets a new version prefix.

Health probes (`/health/live`, `/health/ready`) and the OpenAPI document are outside the versioned
API; see [Operations](operations.md#health-endpoints).

## Requests and responses

- JSON with camelCase names; enumerations are strings (`"succeeded"`, not `2`).
- Timestamps are ISO 8601 in UTC (`2026-10-30T19:59:54.783Z`).
- Identifiers are opaque strings; do not parse them.

## Errors

Every error response is a problem details document ([RFC 9457](https://www.rfc-editor.org/rfc/rfc9457))
with the media type `application/problem+json`:

```json
{
  "type": "https://bexy315.github.io/autocanvas/technical/api#not-found",
  "title": "Not Found",
  "status": 404,
  "traceId": "00-4ce228049aa67859a385f430dd3d98ff-7040738aec8a3621-00"
}
```

`type` is stable and points to one of the sections below; clients branch on it, not on `title`.
`traceId` matches the server log, so an administrator can find what happened. Internal details such
as exception messages never appear in a response.

### Validation

Status 400 or 422. The request is malformed or breaks a rule. Problems about automation files
carry the same diagnostics as the command line: code, message, line and column.

### Unauthorized

Status 401. The request has no valid session or API token.

### Forbidden

Status 403. The caller is known but lacks the role this action needs.

### Not found

Status 404. The resource does not exist, or the caller may not see it — the two are
indistinguishable on purpose, so the API does not reveal what exists in folders a caller cannot
access.

### Conflict

Status 409. The request clashes with the current state, for example a name that is already taken
in the folder.

### Precondition failed

Status 412. The resource changed since the caller read it: the `If-Match` header no longer matches
its `ETag` (see [Concurrency](#concurrency)).

### Internal

Status 500 and above. Something went wrong on the server; the `traceId` identifies it in the log.

## Paging

*From S1 step 4.* Lists return at most `limit` items (default 50, maximum 200) and a `next` cursor
when more exist. Pass it back as `cursor` to get the next page. Cursors are opaque and stay valid
while the list changes, so items are neither skipped nor repeated.

## Concurrency

*From S1 step 5.* Resources that people edit (folders, automations) return an `ETag` header. Send
it back as `If-Match` when changing the resource; if someone else changed it in between, the server
answers with [precondition failed](#precondition-failed) instead of overwriting their change.
