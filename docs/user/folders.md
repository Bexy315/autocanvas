---
sidebar_position: 7
---

# Folders and permissions

Everything you build in AutoCanvas lives in folders: automations, and later their runs,
variables and secrets. Folders decide who may see and change what. You give people a **role** on
a folder, and it applies to everything inside it.

## Roles

| Role | May |
|---|---|
| **Viewer** | see the folder, its subfolders and automations (and, once runs exist, their runs and logs) |
| **Operator** | everything a Viewer may, and start and cancel runs, read variable values |
| **Editor** | everything an Operator may, and create, rename and move subfolders and automations, set variables and secrets |
| **Owner** | everything an Editor may, and grant roles, mark the folder restricted, delete it |

Roles can be granted to **users** and to **groups**; grant to groups where you can, so people who
join or leave a team only need to be added to or removed from its group. If someone holds several
roles on a folder — directly, through groups, and from folders above — the highest one counts.

Admins have every role on every folder.

## Inheritance and restricted folders

A role on a folder also applies to all folders below it. To keep part of a tree away from people
who can see the rest, mark that folder **restricted**: roles from above stop there, and only roles
granted on the restricted folder itself (or below it) count.

```
Team                  ops: Viewer, ada: Owner
├── Docs              → ops: Viewer, ada: Owner   (inherited)
└── Prod (restricted) → nobody from above
    └── App           → bob: Editor               (granted on App)
```

Whoever marks a folder restricted keeps access to it: they automatically get an explicit Owner
grant on it.

## Creating and organizing folders

- Anyone may create a folder at the top level and becomes its Owner.
- A subfolder needs the Editor role on the folder it is created in, and inherits from there.
- Names have 1 to 100 characters without `/` and are unique among siblings regardless of case.
  Top-level names are unique for the whole installation.
- Folders nest at most 16 levels deep.
- Moving a folder takes everything below it along and needs the Editor role on the folder and
  on its new parent; moving it to the top level needs the Owner role. Because roles are inherited,
  **moving a folder changes who can access it**. If you would lose your own access by the move,
  you keep your role through an explicit grant.
- Only empty folders can be deleted, by an Owner; their grants go with them.

## What you see

`GET /api/v1/folders` lists where you can start: the topmost folders you may see. If your access
starts deep in a tree, you see the names of the folders above it for orientation, but cannot
open them. Folders you may not see at all answer *not found*, exactly like folders that do not
exist, so nobody learns what exists in places they have no access to.

## With the API

```sh
# Create a folder, a subfolder, and let the group "ops" view the folder
curl -b cookies.txt -H 'X-AutoCanvas-Request: 1' -H 'Content-Type: application/json' \
  -d '{"name":"Team"}' http://localhost:5080/api/v1/folders
curl -b cookies.txt -H 'X-AutoCanvas-Request: 1' -H 'Content-Type: application/json' \
  -d '{"name":"Prod","parentId":"<team id>"}' http://localhost:5080/api/v1/folders
curl -b cookies.txt -X PUT -H 'X-AutoCanvas-Request: 1' -H 'Content-Type: application/json' \
  -d '{"groupId":"<ops id>","role":"Viewer"}' http://localhost:5080/api/v1/folders/<team id>/grants

# Restrict Prod
curl -b cookies.txt -X PUT -H 'X-AutoCanvas-Request: 1' -H 'Content-Type: application/json' \
  -d '{"isRestricted":true}' http://localhost:5080/api/v1/folders/<prod id>/restriction
```

Every folder answer carries an `ETag`. Send it back as `If-Match` when you change the folder; if
someone else changed it in between, you get `412` instead of overwriting their change (see
[API conventions](../technical/api.md#concurrency)).
