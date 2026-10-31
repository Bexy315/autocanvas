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

## Authentication

Browsers sign in with `POST /api/v1/auth/sign-in` (user name and password). The answer sets the
session cookie `autocanvas_session` (`HttpOnly`, `SameSite=Strict`, `Secure` behind HTTPS); the
session token never appears in a response body. Scripts and tools use **API tokens** instead:
`Authorization: Bearer acp_…`. A token acts with its owner's current permissions; it cannot manage
tokens or sessions or change the password, which needs a session. Without valid credentials,
protected endpoints answer [401](#unauthorized).

**Every request that changes something** (`POST`, `PUT`, `PATCH`, `DELETE` under `/api`) must send
the header `X-AutoCanvas-Request: 1`, unless it carries an API token. Browsers do not let other
websites set this header, so a foreign page cannot make a signed-in browser change anything (cross-
site request forgery). Sign-in needs it too. Without it, the server answers [403](#forbidden).

A user whose password was set by someone else can only read `GET /api/v1/me`, change the password
with `PUT /api/v1/me/password` and sign out until the password is changed; everything else answers
[403 password change required](#password-change-required).

## Requests and responses

- JSON with camelCase names; enumerations are strings (`"succeeded"`, not `2`).
- A required property that is missing or `null` makes the request [invalid](#validation).
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

Status 400. The request is malformed or breaks a rule. `detail` says what is wrong and how to fix
it; `field` names the request property at fault, when there is one:

```json
{
  "type": "https://bexy315.github.io/autocanvas/technical/api#validation",
  "title": "Invalid request",
  "status": 400,
  "detail": "The password must have at least 12 characters.",
  "field": "password"
}
```

Problems about automation files will carry the same diagnostics as the command line: code, message,
line and column.

### Unauthorized

Status 401. The request has no valid session or API token, or a sign-in failed. A failed sign-in
gives the same answer whether the user name is unknown, the password wrong or the account disabled.

### Forbidden

Status 403. The caller is known but lacks the role this action needs, or a request that changes
something lacks the `X-AutoCanvas-Request` header (see [Authentication](#authentication)).

### Password change required

Status 403. The user's password was set by someone else and must be changed first, with
`PUT /api/v1/me/password`.

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

### Too many requests

Status 429. Too many sign-in attempts: either from one network address (10 per minute; the
`Retry-After` header says how many seconds to wait), or for one user name (5 failures within 15
minutes lock it for 15 minutes).

## Paging

Lists return at most `limit` items (default 50, maximum 200) and a `next` cursor
when more exist. Pass it back as `cursor` to get the next page. Cursors are opaque and stay valid
while the list changes, so items are neither skipped nor repeated.

## Concurrency

Resources that people edit return their version as an `ETag` header (`"3"`); folders do so today,
automations will from S1 step 6. Send it back as `If-Match` when changing the resource. If someone
else changed it in between, the server answers [412 precondition failed](#precondition-failed)
instead of overwriting their change; read it again and repeat yours. `If-Match` is optional:
without it (or with `*`) the change applies unconditionally. Weak ETags (`W/"3"`) are accepted.
