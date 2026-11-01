---
sidebar_position: 11
---

# Runners

A **runner** is a program on a machine of yours that executes jobs for the AutoCanvas server. It
connects out to the server, so it needs no open ports itself. The server offers it the queued jobs
it can take, sends their steps one at a time, and decides from what the runner reports whether a
job succeeded.

:::note Status

The reference runner runs jobs in containers with their services, checkouts, script steps and the
node types `docker_build` and `http_request`, with logs, outputs and reports. Runners are managed
through the HTTP API below; the web UI comes with S3.

:::

## Running the reference runner

The reference runner (`autocanvas-runner`) runs each job in a container of its own on a Docker
host: it pulls the job's `image` (or its default image), starts a container with the job's
workspace at `/workspace`, and runs every step in it with `docker exec`. It needs:

- access to a Docker engine — the socket (`/var/run/docker.sock`), or whatever `DOCKER_HOST` or the
  active Docker context names;
- a directory of its own for its credential and the jobs' workspaces (the data directory);
- a connection to the server's runner port (by default `5081`).

Jobs never get the Docker socket. Whoever holds it is root on the Docker host, so the runner is
trusted with the host, while the jobs it runs are not.

### In a container

Build the image from the repository (`docker build -f src/AutoCanvas.Runner/Dockerfile -t
autocanvas-runner .`; it is published with the first release). Mount the Docker socket and the data
directory **at the same path on both sides** — Docker resolves the bind mounts of job containers on
the host, not inside the runner's container:

```sh
docker run --rm -it \
  -v /var/lib/autocanvas-runner:/var/lib/autocanvas-runner \
  -v /var/run/docker.sock:/var/run/docker.sock --group-add "$(stat -c %g /var/run/docker.sock)" \
  -e AUTOCANVAS_RUNNER__SERVER=http://autocanvas.example.org:5081 \
  autocanvas-runner register --token acrt_… --name build-1

docker run -d --name autocanvas-runner --restart unless-stopped \
  -v /var/lib/autocanvas-runner:/var/lib/autocanvas-runner \
  -v /var/run/docker.sock:/var/run/docker.sock --group-add "$(stat -c %g /var/run/docker.sock)" \
  -e AUTOCANVAS_RUNNER__SERVER=http://autocanvas.example.org:5081 \
  -e AUTOCANVAS_RUNNER__LABELS__0=linux -e AUTOCANVAS_RUNNER__LABELS__1=docker \
  autocanvas-runner
```

The image runs as a user without root rights (`app`, id 1654): create the data directory for it first
(`sudo install -d -o 1654 /var/lib/autocanvas-runner`); `--group-add` gives it the socket's group. If the
data directory cannot have the same path on the host, set `Runner:HostDataDirectory` to its path on
the host.

### Directly on a machine

With the .NET runtime installed, `dotnet AutoCanvas.Runner.dll register …` and then
`dotnet AutoCanvas.Runner.dll` from the published runner work the same way; from a checkout,
`dotnet run --project src/AutoCanvas.Runner -- register …`.

### Settings

Settings come from `appsettings.json` next to the runner, from environment variables
(`AUTOCANVAS_RUNNER__SERVER` for `Runner:Server`, `AUTOCANVAS_RUNNER__LABELS__0` for the first
label) or from arguments (`--Runner:Server=…`).

| Setting | Default | Meaning |
|---|---|---|
| `Runner:Server` | none — **required** | The server's runner endpoint, `http://` or `https://` |
| `Runner:DataDirectory` | `data` (the image: `/var/lib/autocanvas-runner`) | The credential (`credential.json`) and the jobs' directories (`work/`); relative to the working directory |
| `Runner:HostDataDirectory` | empty | The data directory's path on the Docker host, when the runner runs in a container and the paths differ |
| `Runner:Labels` | `linux` | The runner's labels, a list |
| `Runner:MaxConcurrentJobs` | `1` | Jobs at a time, 1 to 64 |
| `Runner:DefaultImage` | `ubuntu:24.04` | The image of jobs that name none |
| `Runner:GitImage` | `alpine/git:v2.52.0` | The image checkouts run git in |
| `Runner:BuildKitImage` | `moby/buildkit:v0.34.0-rootless` | Rootless BuildKit, which `docker_build` builds with |
| `Runner:BuildCacheVolume` | `autocanvas-buildkit-cache` | The Docker volume BuildKit keeps its cache in |
| `Runner:ShutdownTimeout` | `00:10:00` | How long a stopping runner lets running jobs finish before it cancels them |

### What a job sees

- **The workspace** is `/workspace`, the working directory of every step unless the step names
  another `working_directory` inside it. It starts empty and is deleted when the job ends.
- **Script steps** run with the shell they ask for, `bash` by default. The runner offers `bash` and
  `sh`; an image without the shell fails the step as an error of the runner, which
  `continue_on_error` does not cover. Use `shell: sh` for images such as Alpine.
- **Outputs**: `AUTOCANVAS_OUTPUT` names a file outside the workspace; lines `name=value` written
  to it become the step's outputs.
- **Services** run next to the job's container on a Docker network of the job's own, where steps
  reach each by its name (`db`, `cache`). The runner tries a service's `ready` command every second
  until it succeeds; a service that stops or does not get ready in time fails the job with its last
  log lines.
- **Checkouts** are cloned by git in a helper container before the first step, into the directory
  each names. A `ref` that is a commit SHA is fetched as exactly that commit. Credentials reach git
  as an HTTP header, never in the clone's `.git/config`.
- **Node steps**: `http_request` sends from the runner itself; `docker_build` runs BuildKit in a
  container of its own, see [Node types](nodes.md).
- **Stopping**: when a job is cancelled or a step runs out of time, the step's shell gets
  `SIGTERM`. A step still running ten seconds later ends with a restart of the job's container,
  which ends every process in it; the workspace stays for steps that follow.
- **Reports** are collected from the workspace after the steps, also when one failed, and uploaded
  to the server. A report whose `path` matches no file is a warning of the job.

### Connection and shutdown

The runner reconnects by itself when its connection breaks — after one second, then doubling up to
a minute — and its jobs run on meanwhile. If the server refuses its credential (the runner was
deleted), it stops with exit code 2; register it again. On `SIGTERM` (`docker stop`) it takes no
new jobs, lets running ones finish for `Runner:ShutdownTimeout`, and cancels the rest; give
`docker stop` a timeout that long (`-t`). When it starts, it removes containers and job directories
an earlier run of it left behind, after a crash say; containers of other runners on the same Docker
host stay.

Managing runners is for **admins**.

## Registering a runner

1. Create a registration token:

   ```sh
   curl -X POST https://autocanvas.example.org/api/v1/runner-registration-tokens \
     -H "Authorization: Bearer $AUTOCANVAS_TOKEN" -H "Content-Type: application/json" \
     -d '{"description": "build box in the basement"}'
   ```

   The answer contains the token (`acrt_…`) once. It works for one registration and expires after
   24 hours unless you give another `expiresAt`, at most 30 days away.
2. Pass the token to the runner's `register` command together with the server's runner address
   (by default port `5081`, see [Operations](../technical/operations.md#runners)):
   `autocanvas-runner register --token acrt_… --name build-1`. The runner exchanges it for its own
   credential and keeps that in its data directory (`credential.json`, readable by its user only);
   see [Running the reference runner](#running-the-reference-runner).

A registration token that is no longer needed can be deleted
(`DELETE /api/v1/runner-registration-tokens/{id}`); a runner that already registered with it stays.

## What a runner can do

When it connects, a runner announces what it can do, and the server offers it only jobs that fit:

| Announced | A job needs it when |
|---|---|
| **Labels**, such as `linux` or `gpu` | its `runs_on` lists the label — a runner must have all of them |
| **Executor**: container or host | it has an `image` or `services` (needs the container executor) |
| **Node types**, such as `http_request` | one of its steps uses the node type |
| **Jobs at a time** | — the runner gets no more jobs than it runs at once |

`GET /api/v1/runners` lists every runner with what it last announced, when it was last seen and
whether it is **online** (heard from within the last two heartbeats).

## Pausing, renaming and removing

- **Pause** a runner, for example before maintenance, with
  `PATCH /api/v1/runners/{id}` and `{"isPaused": true}`. It stays connected and finishes the jobs
  it has, but is offered no new ones; `{"isPaused": false}` resumes it. The same request renames it
  with `"name"`. Names are unique, regardless of case.
- **Delete** a runner with `DELETE /api/v1/runners/{id}`. Its credential stops working at once; a
  connected runner is told not to reconnect. Jobs it still held are offered to other runners or
  fail, as their `retry` settings say.

Every change is in the [audit log](../technical/audit.md#actions).

## When a runner disappears

A runner holds each of its jobs with a **lease** that its heartbeats renew. If the runner loses its
connection, it keeps working and reconnects; jobs it still holds then continue. If it stays away
longer than the lease (one minute by default), its jobs are given up: an offered job goes to another
runner, a running one fails and is retried if its `retry` allows. When the server itself stops,
connected runners are told to reconnect a few seconds later.
