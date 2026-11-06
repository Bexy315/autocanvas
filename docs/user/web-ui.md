---
title: Your first automation
sidebar_label: Your first automation
---

# Your first automation

This tutorial takes one automation from nothing to a shared, tested result in the web UI. You
build it on the canvas, save it, run it on a runner, read its tests and coverage, let a webhook
start it, and share its folder with a team. It takes about fifteen minutes.

The pictures come from the same walk-through that runs in the project's CI on a real server with a
runner, so what you see is what you get.

:::info Before you start

- **A running server.** [Installation](installation.md) sets one up with a runner beside it.
- **An account that may create things.** The first admin from the installation can. Anyone else
  needs the Editor role on a folder ([Folders and permissions](folders.md)).
- **A runner that is online.** The installation's bundled runner is one; Administration →
  **Runners** shows it ([Runners](runners.md)).

:::

## 1. Create a folder

Folders hold automations and decide who may do what with them.

1. Choose **Automations** in the sidebar.
2. Choose **New folder**, name it `Shop` and choose **Create folder**.

The folder opens. Its tabs are **Contents**, **Repositories**, **Configuration** (variables and
secrets) and **Sharing** (who has which role). The tree on the left shows the folder, with its
automations once there are some; the badge next to the name is your role.

![A folder with one automation](img/tour-folder-light.png#gh-light-mode-only)
![A folder with one automation](img/tour-folder-dark.png#gh-dark-mode-only)

## 2. Create an automation

1. In the folder, choose **New automation**.
2. Name it `checkout`, keep **Start from the starter file**, and give the first version a message,
   such as `Started`. Every save has a message, like a commit; it shows in the automation's
   **Versions**.
3. Choose **Create automation**.

![Creating an automation](img/tour-new-automation-light.png#gh-light-mode-only)
![Creating an automation](img/tour-new-automation-dark.png#gh-dark-mode-only)

The automation's page opens with its overview. The starter file has one job, `hello`, that prints a
greeting and writes a small test and coverage report.

## 3. Change it on the canvas

Choose the **Editor** tab. The canvas and the YAML file show the same automation side by side; a
change on one side shows on the other at once, and the file keeps your comments and order.
**Canvas**, **Split** and **YAML** at the top choose what is shown.

![The editor: canvas and file side by side](img/tour-editor-light.png#gh-light-mode-only)
![The editor: canvas and file side by side](img/tour-editor-dark.png#gh-dark-mode-only)

Now name the first job and add a second one that runs after it:

1. Choose the `hello` card. Its settings open on the right; in the **Job** tab, set the **Id** to
   `test`.
2. Choose **Job** in the panel at the canvas's left edge (or drag it onto the canvas). A card
   `job` appears.
3. Drag from the green dot under `test` (its success output) to the new card. The new job now waits
   for `test`.
4. Choose the new card. In its **Job** tab, set the **Id** to `smoke`; in its **Steps** tab, choose
   **Add step** → **Script** and write `echo "It works"`.

![A job's settings next to the canvas](img/tour-canvas-light.png#gh-light-mode-only)
![A job's settings next to the canvas](img/tour-canvas-dark.png#gh-dark-mode-only)

While you type, the server checks the file. Problems are listed under **Problems** at the bottom and
marked at their card and line ([Problems in automation files](diagnostics.md)).

:::tip The editor at a glance

- **Adding**: the panel on the left offers jobs, switches, joins, `finally` jobs and node types,
  with a filter. Next to a card's settings or on a narrow canvas it shrinks to icons.
- **Settings**: a job's panel has two tabs. **Job** holds its sections (needs, runner labels and
  image, environment, services, checkout, reports, limits, outputs), each saying what it holds
  while folded. **Steps** lists the steps; choosing one opens it.
- **Values**: below an empty field it says what applies instead. A variable or secret in a value
  shows as a chip, and the `{}` button inserts the ones that reach the automation.
- **Keyboard**: Ctrl+Z and Ctrl+Y undo and redo, Delete removes the chosen card, Ctrl+S saves,
  Ctrl+F searches the file, Ctrl+Space or Ctrl+I suggests keys and values. **Tidy up** lays the cards
  out again.

:::

## 4. Save a version

Press **Ctrl+S** (or choose **Save**), write a message such as `Add a smoke test`, and press
**Enter**. The automation is at version 2. The file must have no errors to be saved.

## 5. Run it and follow the log

1. Choose the automation's **Runs** tab and **Run**.
2. The run page opens. It shows the jobs as on the canvas, each with its state, and the chosen job
   on the right: its attempts, its steps and the log, which follows live while the job runs.

![A run with its live log](img/tour-run-log-light.png#gh-light-mode-only)
![A run with its live log](img/tour-run-log-dark.png#gh-dark-mode-only)

The log can be searched, wrapped, shown with times and downloaded. **Runs** in the sidebar lists the
recent runs of every automation you can see.

![The runs of an automation](img/tour-runs-light.png#gh-light-mode-only)
![The runs of an automation](img/tour-runs-dark.png#gh-dark-mode-only)

## 6. Read the tests and coverage

The `test` job collects a test report and a coverage report, so the run page has the tabs
**Tests** and **Coverage**.

- **Tests**: failed tests come first, with their message and stack trace. The clock next to a test
  opens its history across runs, which also tells whether it looks flaky.
- **Coverage**: the share of lines reached, per report, package and class, with the lines that were
  not reached.

![The failed test of a run](img/tour-tests-light.png#gh-light-mode-only)
![The failed test of a run](img/tour-tests-dark.png#gh-dark-mode-only)

![Coverage by class](img/tour-coverage-light.png#gh-light-mode-only)
![Coverage by class](img/tour-coverage-dark.png#gh-dark-mode-only)

## 7. Let a webhook start it

So far the automation runs only by hand. To let another system start it:

1. In the editor, choose the **Trigger** card and check **By a webhook**; save.
2. On the **Triggers** tab, choose **Create address**.

The address and its signing secret are shown once, with example calls for curl and PowerShell. Keep
them; a call to that address starts a run.

![A new webhook address](img/tour-webhook-light.png#gh-light-mode-only)
![A new webhook address](img/tour-webhook-dark.png#gh-dark-mode-only)

## 8. Share the folder

The folder's **Sharing** tab is for its owners. **Share** finds people and groups by name and gives
them a role on the folder and everything in it: Viewer, Operator (may start runs), Editor or Owner.

![A folder shared with a group](img/tour-sharing-light.png#gh-light-mode-only)
![A folder shared with a group](img/tour-sharing-dark.png#gh-dark-mode-only)

## What's next

You have built, saved, run, triggered and shared an automation. From here:

- **Keep automations in Git**: connect a repository to the folder, and its automation files become
  automations that run on every push ([Repositories](repositories.md), [GitHub](github.md)).

  ![A folder's repositories](img/repositories-light.png#gh-light-mode-only)
  ![A folder's repositories](img/repositories-dark.png#gh-dark-mode-only)

- **Configure without editing the file**: variables and secrets per folder
  ([Variables and secrets](variables-and-secrets.md)).
- **Start runs on a schedule or after another automation** ([Triggers](triggers.md)).
- **Write the file by hand**: every key of the format ([Automation files](automations.md)).
- **Light or dark**: the user menu at the top right switches; your **Account** there changes your
  password and lists your sessions and API tokens.
