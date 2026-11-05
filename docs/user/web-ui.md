---
sidebar_position: 1.5
---

# A tour of the web UI

The web UI comes with the server ([Installation](installation.md)): open its address in a browser
and sign in. The first admin signs
in with the password set at installation and chooses their own right away; everyone else gets an
account from an admin ([Users and groups](accounts.md)).

This tour follows one automation from start to end: it is built on the canvas, saved, started on a
runner, and its tests and coverage are read; then a webhook starts it and its folder is shared with
a team. The pictures are taken by the same walk-through in the project's CI, on a real server with a
runner.

## Folders and automations

**Automations** in the sidebar shows your folders on the left and the chosen folder on the right:
its automations and folders, and the tabs **Configuration** (its variables and secrets) and
**Sharing** (who has which role). The badge next to the name is your role here.

![A folder with one automation](img/tour-folder-light.png#gh-light-mode-only)
![A folder with one automation](img/tour-folder-dark.png#gh-dark-mode-only)

**New automation** starts one from a small starter file, or from a file of your own. Each save
needs a short message, like a commit; the message shows in the automation's **Versions**.

![Creating an automation](img/tour-new-automation-light.png#gh-light-mode-only)
![Creating an automation](img/tour-new-automation-dark.png#gh-dark-mode-only)

More: [Folders and permissions](folders.md), [Automation files](automations.md).

## Building on the canvas

**Editor** opens the automation in the full window. The canvas and the YAML file show the same
automation side by side, and a change on one side shows on the other at once: the file stays as you
wrote it, with its comments and order. **Canvas**, **Split** and **YAML** choose what is shown.

![The editor: canvas and file side by side](img/tour-editor-light.png#gh-light-mode-only)
![The editor: canvas and file side by side](img/tour-editor-dark.png#gh-dark-mode-only)

- **Adding**: the panel at the canvas's left edge adds a job, a switch, a join, a `finally` job or a
  node type; choose one or drag it onto the canvas.
  - **Filter**: a filter narrows the list.
  - **When space is short**: next to a card's settings or on a narrow canvas, the panel shrinks to
    icons, whose tooltips name them.
  - **Hiding**: it can be hidden, and the browser remembers that.
- **Wiring**: drag from a card's outputs (green on success, red on failure, grey either way) to the
  next card, which then waits for it.
- **Settings**: choosing a card opens its settings on the right: the job's image and runner labels,
  its steps, services, reports, limits and outputs. The trigger card holds the automation's own
  settings, such as its schedules and whether a webhook may start it.
- **Keyboard**: Ctrl+Z and Ctrl+Y undo and redo, Delete removes the chosen card, Ctrl+S saves.
  **Tidy up** lays the cards out again.

![A job's settings next to the canvas](img/tour-canvas-light.png#gh-light-mode-only)
![A job's settings next to the canvas](img/tour-canvas-dark.png#gh-dark-mode-only)

Problems in the file are listed below the editor while you type, at the card or the line they are
in. More: [Automation files](automations.md), [Node types](nodes.md).

## Starting and following a run

**Run** on the automation's **Runs** tab starts it by hand. The run page shows its jobs as on the
canvas, each with its state, and the chosen job on the right: its attempts, its steps and the log,
which follows live while the job runs. The log can be searched, wrapped, shown with times and
downloaded.

![A run with its live log](img/tour-run-log-light.png#gh-light-mode-only)
![A run with its live log](img/tour-run-log-dark.png#gh-dark-mode-only)

**Runs** in the sidebar lists the recent runs of every automation you can see.

![The runs of an automation](img/tour-runs-light.png#gh-light-mode-only)
![The runs of an automation](img/tour-runs-dark.png#gh-dark-mode-only)

More: [Runs](runs.md).

## Tests and coverage

When a job collects test or coverage reports, the run page gets the tabs **Tests** and
**Coverage**. Failed tests come first, with their message and stack trace; the clock next to a test
opens its history across runs, which also tells whether it looks flaky.

![The failed test of a run](img/tour-tests-light.png#gh-light-mode-only)
![The failed test of a run](img/tour-tests-dark.png#gh-dark-mode-only)

**Coverage** shows the share of lines reached, per report, package and class, with the lines that
were not reached.

![Coverage by class](img/tour-coverage-light.png#gh-light-mode-only)
![Coverage by class](img/tour-coverage-dark.png#gh-dark-mode-only)

More: [Test and coverage reports](reports.md).

## Triggers

The **Triggers** tab lists what starts the automation: by hand, schedules with their next time,
other automations, and webhooks. **Create address** makes a webhook address with a signing secret;
both are shown once, with example calls for curl and PowerShell.

![A new webhook address](img/tour-webhook-light.png#gh-light-mode-only)
![A new webhook address](img/tour-webhook-dark.png#gh-dark-mode-only)

More: [Triggers](triggers.md), [Variables and secrets](variables-and-secrets.md).

## Repositories

A folder's **Repositories** tab connects Git repositories, from GitHub through the GitHub App or by
address, whose automation files become automations of the folder; it shows how each was last read,
syncs it, and makes webhook addresses.

![A folder's repositories](img/repositories-light.png#gh-light-mode-only)
![A folder's repositories](img/repositories-dark.png#gh-dark-mode-only)

More: [Repositories](repositories.md), [GitHub](github.md).

## Sharing

A folder's **Sharing** tab is for its owners: it gives people and groups a role on the folder and
everything in it. **Share** finds them by name.

![A folder shared with a group](img/tour-sharing-light.png#gh-light-mode-only)
![A folder shared with a group](img/tour-sharing-dark.png#gh-dark-mode-only)

More: [Folders and permissions](folders.md). Admins manage users, groups and runners under
**Administration** ([Users and groups](accounts.md), [Runners](runners.md)).

## Light and dark

The user menu at the top right switches between light, dark and the system's choice; the browser
remembers it. Your **Account** there changes your password and lists your sessions and API tokens.
