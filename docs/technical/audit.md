---
sidebar_position: 3
---

# Audit log

AutoCanvas records every change to its configuration in an append-only audit log: who did it, what
changed, which object, when and from where. This page describes what an entry contains and lists
every action. The rules behind it are in the [Security model](security.md#audit).

## Entries

| Field | Meaning |
|---|---|
| `occurredAt` | When the change was committed, in UTC |
| actor | `System` (the server, a command-line tool or a background job), `User` (signed in), `Token` (a user's API token), `Runner` (a runner, with its credential) or `Anonymous` (a client that is not signed in); for users and runners also their id and their name at the time |
| `action` | What happened, from the catalogue below |
| target | Kind and id of the object changed, if any |
| folder | The folder the object lives in; folder owners can read the entries of their folders |
| details | A few facts about the change, such as a new name. **Never secret values** — for a secret only its name |
| client | Network address and user agent, for changes made over HTTP |

An entry is written in the same database transaction as the change it describes: a change that
fails leaves no entry, and an entry never describes a change that did not happen. Entries are
never changed through AutoCanvas; only the retention below deletes old ones.

## Reading the log

| Request | Who | Returns |
|---|---|---|
| `GET /api/v1/audit` | admins | every entry |
| `GET /api/v1/folders/{id}/audit` | the folder's owners | entries of the folder and of the folders below it that the caller owns too |

A restricted subfolder the caller does not own stays out of their view, like everywhere else.
Entries of deleted folders can only be read by admins.

Both list the newest entries first, one page at a time (`limit`, `cursor` as for every list), and
take the same filters, which combine:

| Parameter | Matches |
|---|---|
| `from`, `to` | `occurredAt` from (inclusive) and to (exclusive), e.g. `2026-11-01T00:00:00Z` |
| `action` | an action, or a prefix ending in `*`: `folder.*` matches every folder action |
| `actorId` | changes by this user, in person or through their API tokens |
| `targetType`, `targetId` | changes to this kind of object, or to one object |

```sh
curl -H "Authorization: Bearer $AUTOCANVAS_TOKEN" \
  "https://autocanvas.example.org/api/v1/audit?action=secret.*&from=2026-11-01T00:00:00Z"
```

In responses, `details` is a JSON object, and the actor is given as `actorKind`, `actorId` and
`actorName`.

## Retention

Entries older than `Audit:RetentionDays` (default **365**) are deleted once a day, starting when
the server has started; `0` keeps them forever. Each run that deletes something records
`audit.pruned` with the cut-off time and the number of entries, so the log shows where it begins.
To keep entries longer than the retention allows, export them regularly, for example with the
query above.

## Actions

Action names are stable: they are stored and used in queries. New actions are added here when the
feature that records them is built.

| Action | Actor | Target | Details |
|---|---|---|---|
| `database.migrated` | System | — | `migrations`: names of the schema migrations applied, in order |
| `user.created` | admin, System | user | `userName`, `isAdmin` |
| `user.updated` | admin | user | `changed` (field names), `isAdmin`, `isDisabled` |
| `user.deleted` | admin | user | `userName` |
| `user.password_reset` | admin, System | user | — |
| `user.password_changed` | the user | user | — |
| `auth.signed_in` | the user | user | `sessionId` |
| `auth.sign_in_failed` | Anonymous | — | `userName` as typed, `reason` (`unknown user`, `wrong password`, `disabled`, `locked`) |
| `auth.signed_out` | the user | user | `sessionId` |
| `auth.sessions_ended` | the user | user | `count` (sign out everywhere) |
| `token.created` | the user | user (owner) | `tokenId`, `name`, `expiresAt` — never the token |
| `token.deleted` | the owner or an admin | user (owner) | `tokenId`, `name` |
| `folder.created` | the creator | folder | `name`, `parentId` |
| `folder.updated` | an editor | folder | `changed` (field names), `name` |
| `folder.moved` | an editor | folder | `fromParentId`, `toParentId` (empty for the top level) |
| `folder.restriction_changed` | an owner | folder | `isRestricted` |
| `folder.deleted` | an owner | folder | `name`, `parentId` |
| `folder.grant_set` | an owner, or the system for an actor keeping access | folder | `subjectType` (`user`/`group`), `subjectId`, `subjectName`, `role`, `previousRole` |
| `folder.grant_removed` | an owner | folder | `subjectType`, `subjectId`, `subjectName`, `role` |
| `automation.created` | an editor | automation | `name` |
| `automation.saved` | an editor | automation | `version`, `message` — never the file |
| `automation.renamed` | an editor | automation | `from`, `to` |
| `automation.moved` | an editor | automation | `fromFolderId`, `toFolderId` |
| `automation.deleted` | an editor | automation | `name`, `versions` |
| `variable.set` | an editor, or an admin for the instance | variable | `scope`, `name` — never the value |
| `variable.deleted` | an editor, or an admin for the instance | variable | `scope`, `name` |
| `secret.set` | an editor, or an admin for the instance | secret | `scope`, `name` — never the value |
| `secret.deleted` | an editor, or an admin for the instance | secret | `scope`, `name` |
| `secrets.master_key_rotated` | the command line | — | `fromKeyId`, `toKeyId`, `secrets` |

Every folder action carries the folder in the *folder* field, so a folder's owners can read its
part of the log.
| `group.created` | admin | group | `name` |
| `group.updated` | admin | group | `changed` (field names), `name` |
| `group.deleted` | admin | group | `name` |
| `group.member_added` | admin | group | `userId`, `userName` |
| `group.member_removed` | admin | group | `userId`, `userName` |
| `audit.pruned` | System | — | `before` (cut-off time), `deleted` (number of entries) |
| `run.started` | an operator | run | `automation`, `number` |
| `run.cancelled` | an operator | run | `automation`, `number` |
| `runner.token_created` | admin | runner_token | `tokenId`, `description`, `expiresAt` |
| `runner.token_deleted` | admin | runner_token | `tokenId`, `description` |
| `runner.registered` | the runner | runner | `name`, `tokenId` |
| `runner.updated` | admin | runner | `name`, `changed` (field names), `isPaused` |
| `runner.deleted` | admin | runner | `name` |
