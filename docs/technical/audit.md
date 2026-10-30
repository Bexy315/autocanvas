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
| actor | `System` (the server, a command-line tool or a background job), `User` (signed in), `Token` (a user's API token) or `Anonymous` (a client that is not signed in); for users also their id and their name at the time |
| `action` | What happened, from the catalogue below |
| target | Kind and id of the object changed, if any |
| folder | The folder the object lives in; folder owners can read the entries of their folders |
| details | A few facts about the change, such as a new name. **Never secret values** — for a secret only its name |
| client | Network address and user agent, for changes made over HTTP |

An entry is written in the same database transaction as the change it describes: a change that
fails leaves no entry, and an entry never describes a change that did not happen. Entries are
never updated or deleted through AutoCanvas; a retention setting for old entries follows in S1
step 8.

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
| `group.created` | admin | group | `name` |
| `group.updated` | admin | group | `changed` (field names), `name` |
| `group.deleted` | admin | group | `name` |
| `group.member_added` | admin | group | `userId`, `userName` |
| `group.member_removed` | admin | group | `userId`, `userName` |
