---
sidebar_position: 9
---

# Variables and secrets

Automations read configuration from **variables** (`${{ vars.NAME }}`) and credentials from
**secrets** (`${{ secrets.NAME }}`), see [Expressions](expressions.md). Both can be defined for the
whole instance and for folders; secrets also for a single automation. Define a value once, high
enough in the folder tree, and every automation below it can use it.

:::note Status

Variables and secrets are managed in the web UI (see [In the web UI](#in-the-web-ui)) and
through the HTTP API, and runs read them along the chain below.

:::

## In the web UI

- **Instance**: **Administration** in the sidebar (admins only) holds the instance's variables and
  secrets.
- **Folders**: a folder's **Configuration** tab holds its own.
- **Automations**: an automation's **Configuration** tab holds its secrets, and shows the
  variables of its file (changed in the editor).

Variables are added, changed and deleted in a list. A secret is set in a dialog with a password
field: its value is never shown, not even the one it replaces.

The **Configuration** tab of a folder and of an automation lists every variable and every secret a
run there reads, each with a tag saying where it is defined:
- **here**: defined at this place;
- **in the file**: an automation's own `variables:`;
- **folder …** (a link to that folder's configuration) or **instance**: reaching it from above.

Those defined here can be changed and deleted there. Those from above offer **Override here**,
which sets the same name at this place, so it wins for everything below. A name that overrides
others says so; unfold it to see every definition from the instance down, those overridden struck
through. Variable values need the Operator role; secret values are never shown. An automation's
own variables are changed in the editor.

## Where values are defined

| Place | Variables | Secrets |
|---|---|---|
| **Instance** | yes, set by admins | yes, set by admins |
| **Folder** | yes | yes |
| **Automation** | in its file, under [`variables:`](automations.md#top-level) | yes |

Variables of one automation live in its file, so they are versioned with it. Secrets never go in a
file: they are stored separately, encrypted.

## Which value wins

A run reads every name along a chain, and the **nearest definition wins**:

1. the instance,
2. the folders from the top level down to the automation's folder,
3. the automation: its secrets, and the `variables:` in its file.

A [restricted folder](folders.md#inheritance-and-restricted-folders) also cuts this chain: folders
above it pass no values into it. Instance values still apply. So someone who only has access to a
restricted folder cannot use secrets of the folders above it.

```
Instance            DOMAIN=example.org   REGION=eu
└── Team            REGION=eu-central    secret TOKEN
    └── App         TIER=web             secret TOKEN (another one)
        └── deploy  file: REGION=eu-west, secret DEPLOY_KEY

deploy reads: DOMAIN=example.org, REGION=eu-west, TIER=web,
              TOKEN from App, DEPLOY_KEY from the automation
```

To see where each value of a folder or automation comes from, ask the server to **explain** it
(see [Using the API](#using-the-api)): every name with all its definitions in chain order and the
one that wins. Secret values are never part of it.

## Names and values

- Names start with a letter or underscore, followed by letters, digits and underscores, at most
  100 characters. They are unique per place regardless of case, and expressions read them
  regardless of case: setting `token` where `TOKEN` exists replaces it.
- Values have at most 64 KiB. Variables may be empty; secrets may not.

## Who may do what

| | See names | See variable values | Change |
|---|---|---|---|
| **Instance** | everyone signed in | everyone signed in | admins |
| **Folder, automation** | Viewer | Operator | Editor |

**Secret values are write-only.** Once set, nobody can read them back through AutoCanvas, not even
admins; only runs receive them, and their logs will show `***` in their place. To change a secret,
set it again. The [audit log](../technical/audit.md) records who set or deleted which name, never
the value.

Deleting a folder or an automation deletes its variables and secrets.

## How secrets are protected

Secrets are encrypted before they reach the database, with a master key that the administrator
keeps outside it. A database backup alone does not reveal them. Setting up, backing up and
rotating that key is described in [Operations](../technical/operations.md#master-key).

## Using the API

The endpoints are described in the API reference (`/openapi/v1.json` on every server).

| Request | Does |
|---|---|
| `GET /api/v1/variables`, `GET /api/v1/secrets` | list the instance's values |
| `PUT /api/v1/variables/{name}`, `PUT /api/v1/secrets/{name}` | create (201) or replace (200): `{"value": "…"}` |
| `DELETE /api/v1/variables/{name}`, `DELETE /api/v1/secrets/{name}` | delete |
| `…/folders/{id}/variables`, `…/folders/{id}/secrets` | the same for a folder |
| `…/automations/{id}/secrets` | the same for an automation's secrets |
| `GET /api/v1/folders/{id}/configuration` | explain what a folder's contents read |
| `GET /api/v1/automations/{id}/configuration` | explain what an automation reads |

```sh
curl -X PUT https://autocanvas.example.org/api/v1/folders/$FOLDER/secrets/TOKEN \
  -H "Authorization: Bearer $AUTOCANVAS_TOKEN" -H "Content-Type: application/json" \
  -d '{"value": "…"}'
```

An explanation looks like this (values shown to Operators):

```json
{
  "variables": [
    {
      "name": "REGION",
      "definitions": [
        { "kind": "Instance", "folderId": null, "folderName": null, "value": "eu" },
        { "kind": "Folder", "folderId": "01a1…", "folderName": "Team", "value": "eu-central" },
        { "kind": "Automation", "folderId": null, "folderName": null, "value": "eu-west" }
      ],
      "winner": { "kind": "Automation", "folderId": null, "folderName": null, "value": "eu-west" }
    }
  ],
  "secrets": [
    {
      "name": "TOKEN",
      "definitions": [{ "kind": "Folder", "folderId": "01a1…", "folderName": "Team", "value": null }],
      "winner": { "kind": "Folder", "folderId": "01a1…", "folderName": "Team", "value": null }
    }
  ]
}
```
