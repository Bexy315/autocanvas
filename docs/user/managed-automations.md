---
sidebar_position: 8
---

# Managed automations

A managed automation is stored by AutoCanvas itself, in a [folder](folders.md), instead of in a
repository. Every save keeps the previous state as a version, so you can see who changed what and
go back. The content is an ordinary [automation file](automations.md).

:::note Status

Managed automations are created, organized, compared, edited on the canvas or as YAML, and run in
the web UI (see [A tour of the web UI](web-ui.md)) and through the HTTP API.

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
another automation), its latest runs, warnings worth a look, and the automation itself: as a canvas
to look at, or as its file (**Canvas** and **File** switch; changes happen in the editor).
**Run** in the page's header starts it by hand, on every tab, for people with the Operator role
when its triggers allow it. **Versions** lists every version with its message, author and time; a
version shows what changed from the one before, with unchanged lines collapsed and canvas positions
(`x-editor`) left out, or its full file. **Restore** saves an old version's file as the newest
version. The menu next to **Run** downloads the file as YAML, and for editors renames, moves and
deletes the automation.

**Editor** opens the automation's editor, which fills the whole window: the app's sidebar and the
folder tree make room for it, and the arrow next to the automation's name leads back. It shows the
automation as a graph on the canvas next to its file. The buttons
**Canvas**, **Split** and **YAML** show either one or both; drag the line between them, or move it
with the arrow keys, to share the width differently. The canvas draws the trigger, a card per job
with its steps, and the lines between them: green when a job waits for success, red for failure,
grey for always, violet for a switch's case, dashed into a `finally` job. It follows the file as you
type; while the file cannot be read, it keeps the last readable state and says so. Choosing a card
shows its job in the file, and the job your cursor is in is highlighted on the canvas. **List**
shows the same as text.

![The editor: canvas and file side by side](img/tour-editor-light.png#gh-light-mode-only)
![The editor: canvas and file side by side](img/tour-editor-dark.png#gh-dark-mode-only)

Editors build on the canvas too. The bar above it adds a **Job**, **Switch**, **Join** or **Finally**
job, or a job with a step of a node type such as `http_request`: drag an entry to a place, or choose
it to add it in the middle. Drag a line from the bottom of a card (green on success, red on failure,
grey either way, or a switch's case) to the top of another to make it wait; choose a line or card and
press Delete to remove it, and Shift-click or Shift-drag to choose several. Drag cards, or move a
chosen one with the arrow keys; **Tidy up** arranges them all. Every change lands in the YAML at once,
changing only the lines it must: your comments, blank lines and formatting stay. **Undo** and
**Redo** (Ctrl+Z, Ctrl+Y) go back and forth through canvas changes and typing alike.

Choose a card (click it, or press Enter on it) to open its **side panel**; Esc or the × closes it, and
it follows when you choose another card or move the cursor in the file to another job. What you type
there lands in the file at once, and one undo takes back a value you typed.

![A job's settings next to the canvas](img/tour-canvas-light.png#gh-light-mode-only)
![A job's settings next to the canvas](img/tour-canvas-dark.png#gh-dark-mode-only)

- **A job**: a rail at the top lists its settings and its steps. **Job settings** holds the id (other
  jobs, its place on the canvas and `${{ jobs.ID… }}` in expressions follow when it changes), name
  and condition; **Needs**, what the job waits for, which is also the way to connect jobs with the
  keyboard; runner labels and image; environment; services (a preset such as Postgres, or an
  image); what to check out; reports; timeout and retries; outputs. Choose a step to edit its
  script, or the inputs of its node type (each may be an expression instead), with more settings
  under **More**. **Add step** adds a script or a node type's step; the arrows reorder steps.
- **A switch**: its expression and cases; **a join**: what it waits for.
- **The trigger**: what starts the automation (by hand, webhook, schedules, pushes, another
  automation finishing) and the settings of the whole file: description, timeout, whether runs may
  overlap, `changed_when` and variables.

Problems the server finds are shown at their field. A value written in a form the panel does not
show, such as an expression in place of a list or an anchor shared with another place, is shown as
written, to be changed in the YAML.

The file is edited in a YAML editor. While you type, the server checks the file exactly
as a save would, including node types and triggers; problems are underlined in the file and listed
under **Problems** at the bottom of the window (which you can fold away) with their
[code](diagnostics.md), and choosing one jumps to its place. Ctrl+Space (or Ctrl+I, where the
system or the browser takes Ctrl+Space) suggests the keys allowed where the cursor is, the node types
for a step's `type`, a node type's inputs under `with`, and fixed values such as
`concurrency: single`. Ctrl+F searches and replaces in the file, also while the canvas has the focus.
**Save** (or Ctrl+S, also on the canvas)
asks for an optional message and says how much changed; Enter in that dialog stores the new version,
Esc goes back without saving. So pressing Ctrl+S out of habit does not create a version each time.
Saving needs a file without errors.
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

## See also

- **Next**: [Automation files](automations.md): every key of the format.
- [Triggers](triggers.md): what starts a run.
- [Runs](runs.md): what happens when it runs.
