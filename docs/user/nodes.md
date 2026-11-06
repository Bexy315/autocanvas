---
sidebar_position: 3
---

# Node types

A step either runs a script (`run`) or uses a **node type** (`type`) with inputs under `with`.
AutoCanvas ships two node types. An automation that names another one is refused when it is saved,
as is one that gives an input a node type does not have, leaves out a required one, or gives one
of the wrong kind (see [Diagnostics](diagnostics.md)). An input whose value is an expression
(`${{ … }}`) is checked when the step runs, since only then is it known.

A job with node steps goes to runners that announce those node types; the reference runner
implements both. `GET /api/v1/node-types` lists them with their inputs and outputs, as the web UI's
editor uses them for suggestions.

## `http_request`

Sends one HTTP request from the runner — not from the job's container — and returns the
response.

```yaml
- id: deploy
  type: http_request
  with:
    method: POST
    url: https://deploy.example.org/api/releases
    headers:
      Authorization: Bearer ${{ secrets.DEPLOY_TOKEN }}
      Content-Type: application/json
    body: '{ "version": "${{ steps.version.outputs.value }}" }'
```

| Input | Default | Meaning |
|---|---|---|
| `url` | — **required** | The address, `http` or `https` |
| `method` | `GET` | `GET`, `HEAD`, `POST`, `PUT`, `PATCH`, `DELETE` or `OPTIONS` |
| `headers` | none | Request headers by name; `Content-Type` applies to the body |
| `body` | none | The request body, as text |
| `timeout_seconds` | `30` | How long to wait for the answer, 1 to 600 |
| `fail_on_status` | `true` | Whether a status of 400 or more fails the step |

| Output | Meaning |
|---|---|
| `status` | The response's status code, such as `200` |
| `body` | The response's body as text, cut after 32 KiB |

A request that gets no answer in time, or cannot be sent at all, fails the step; the log says why.
Secrets used in the inputs are masked in the log and in the outputs.

On the canvas, a step of a node type shows its inputs as a form, with what each one does and what
applies while it is empty:

![An http_request step's inputs in the side panel](img/nodes-http-request-light.png#gh-light-mode-only)
![An http_request step's inputs in the side panel](img/nodes-http-request-dark.png#gh-dark-mode-only)

## `docker_build`

Builds a container image from a Dockerfile in the workspace with rootless
[BuildKit](https://github.com/moby/buildkit), in a container of its own next to the job's: it is
not privileged and never sees the Docker socket. Without `push` the image is only built, which
proves that it builds and gives its digest; with `push: true` it goes to a registry.

```yaml
- id: image
  type: docker_build
  with:
    image: ghcr.io/example/app
    tags: [latest, '${{ run.commit_short_sha }}']
    context: src
    build_args:
      VERSION: ${{ steps.version.outputs.value }}
    push: true
    username: example-bot
    password: ${{ secrets.GHCR_TOKEN }}
```

| Input | Default | Meaning |
|---|---|---|
| `image` | — **required** | The image's name, such as `my-app` or `ghcr.io/team/app`; with `push`, its name in the registry |
| `tags` | `latest` | Tags of the image; a tag written in `image` counts when `tags` is not given |
| `context` | the workspace | The build context, a directory relative to the workspace |
| `dockerfile` | `Dockerfile` | The Dockerfile, relative to the context |
| `build_args` | none | Build arguments (`ARG`) by name |
| `target` | the last stage | The stage of a multi-stage Dockerfile to build |
| `push` | `false` | Whether to push the image |
| `registry` | from `image` | The registry the credentials are for: the first part of `image` if it is a host (`ghcr.io`), otherwise Docker Hub |
| `username`, `password` | none | Credentials for the registry; give the password from a secret |

| Output | Meaning |
|---|---|
| `digest` | The image's digest, `sha256:…` |
| `image` | The image with its first tag, such as `ghcr.io/example/app:latest` |

The runner keeps BuildKit's cache in a Docker volume of its own, so later builds on the same
runner reuse unchanged layers. Credentials are handed to BuildKit for the one build and deleted
afterwards.

## See also

- [Automation files](automations.md#steps): steps around node types.
- [Expressions](expressions.md): reading a step's outputs.
