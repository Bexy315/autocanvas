---
sidebar_position: 8
---

# Managed automations

A managed automation is stored by AutoCanvas itself, in a [folder](folders.md), instead of in a
repository. Every save keeps the previous state as a version, so you can see who changed what and
go back. The content is an ordinary [automation file](automations.md).

:::note Status

Managed automations can be created, organized, read, compared and edited as YAML in the web UI
and through the HTTP API. Editing them on the canvas and running them from the web UI come with the
next steps of S3.

:::

## Who may do what

The folder decides, like for everything else in it:

| Role on the folder | May |
|---|---|
| **Viewer** | list the automations, read them, their history and every version, download the file |
| **Editor** | everything a Viewer may, and create, save, rename, move and delete them |

Moving an automation needs the Editor role on both folders. Because roles come from the folder,
**moving an automation changes who can access it**.

## Names and titles

Every automation has a **name**: its key in the folder, like a file name. Names have 1 to 100
characters without `/` and are unique in their folder regardless of case. The **title** shown in
lists is the `name:` from the file, or the name when the file has none; the description comes from
`description:`.

## Saving and versions

- Every save creates a new **version**, numbered from 1, with the author and an optional message
  saying what changed. Versions are never changed afterwards. If the author's account is deleted
  later, their user name stays on the versions.
- **Only valid files are saved.** A file with errors is refused with every problem, each with its
  [code](diagnostics.md), the place in the document and the line and column. So every stored
  version can run. Warnings do not stop a save; they come back with the automation.
- Saving a file identical to the current version is refused — nothing would change.

`POST /api/v1/automations/validate` runs the checks of a save in the given folder (Viewer is
enough) and always answers 200 with every problem, so editors and scripts can check a file before
saving it; pass `automationId` when the file is an existing automation's, so it is not reported as
following itself.
- To **restore** an old version, read it and save its file again. It becomes the newest version;
  the history in between stays.
- Renaming and moving do not create a version.

## Deleting

Deleting an automation removes it **with all its versions**; this cannot be undone. The
[audit log](../technical/audit.md) keeps who deleted which automation and when, but not its content.
Its [runs](runs.md) stay readable: each keeps its own copy of the file it ran. A folder can only be
deleted once it holds no automations and no subfolders; the runs left in it go with it.

## In the web UI

Open a folder in **Automations** and choose **New automation**. Give it a name and start from the
starter file (started by hand, one job that says hello) or a file from your computer. If the file
has problems, they are listed with their line, column and [code](diagnostics.md); fix the file
and choose it again.

An automation's page shows what starts it (by hand, webhook, schedules with their next time, or
another automation), its latest runs, warnings worth a look, and its file, which **Download** saves
as YAML. **Versions** lists every version with its message, author and time; a version shows what
changed from the one before, with unchanged lines collapsed and canvas positions (`x-editor`) left
out, or its full file. **Restore** saves an old version's file as the newest version. The menu
next to **Download** renames, moves and deletes the automation; it is there for editors only.

**Editor** opens the file in a YAML editor. While you type, the server checks the file exactly
as a save would, including node types and triggers; problems are underlined in the file and listed
below it with their [code](diagnostics.md), and choosing one jumps to its place. Ctrl+Space suggests
the keys allowed where the cursor is, the node types for a step's `type`, a node type's inputs
under `with`, and fixed values such as `concurrency: single`. **Save** (or Ctrl+S) stores a new
version with an optional message; it is offered once the file has changes and no errors.
**Discard** returns to the saved version, and leaving the page with unsaved changes asks first.
People with the Viewer role see the file in the editor without changing it.

If someone else saved the automation while you were editing, your save is not applied over theirs.
Instead the editor shows what your file changes compared with their version, and you choose: save
yours on top as the next version, or load theirs and drop your changes. A folder changed by someone
else in the meantime is refused the same way, and the page shows its current state.

## Using the API

The endpoints are described in the API reference (`/openapi/v1.json` on every server). In short:

| Request | Does |
|---|---|
| `GET /api/v1/folders/{id}/automations` | list the automations in a folder, by name |
| `POST /api/v1/folders/{id}/automations` | create one: `{"name", "yaml", "message"}` |
| `GET /api/v1/automations/{id}` | the automation with the file of its current version |
| `GET /api/v1/automations/{id}/yaml` | download the current file as `text/yaml` |
| `PUT /api/v1/automations/{id}` | save a new version: `{"yaml", "message"}` |
| `PATCH /api/v1/automations/{id}` | rename: `{"name"}` |
| `POST /api/v1/automations/{id}/move` | move to another folder: `{"folderId"}` |
| `DELETE /api/v1/automations/{id}` | delete with all versions |
| `GET /api/v1/automations/{id}/versions` | the history, newest first |
| `GET /api/v1/automations/{id}/versions/{number}` | one version with its file |
| `POST /api/v1/automations/validate` | check a file without saving it: `{"yaml", "folderId", "automationId"}`, answers `{"valid", "diagnostics"}` |

The `ETag` of an automation is its version number. Send it back as `If-Match` when saving, and the
server refuses the save (412) if someone else saved in the meantime instead of overwriting their
change.

A refused file looks like this — here a job needs `tset` instead of `test`:

```json
{
  "type": "https://bexy315.github.io/autocanvas/technical/api#validation",
  "title": "Invalid request",
  "status": 400,
  "detail": "The automation file has a problem; see diagnostics.",
  "field": "yaml",
  "diagnostics": [
    {
      "severity": "Error",
      "code": "AC0201",
      "message": "There is no job 'tset'. Did you mean 'test'?",
      "path": "jobs.image.needs[0]",
      "line": 39,
      "column": 13
    }
  ]
}
```
