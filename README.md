# AutoCanvas

> [!NOTE]
> AutoCanvas is the standalone version of the automation engine in my homelab, which grew out of
> experiments, and it has been written almost entirely by AI coding agents ("vibe coded"). Read and
> test it with that in mind before you rely on it.

Self-hosted automation and CI/CD engine for developers and technicians: pipelines as YAML in your
repository or built visually on a canvas, executed by lightweight runners, with test reports as a
first-class feature.

> **Status: pre-alpha.** No release yet; the milestone plans are in [docs/plans](docs/plans/).

## Quick start

With Docker and Compose v2, from a checkout:

```sh
deploy/install.sh            # Linux, macOS
deploy/install.ps1           # Windows, PowerShell 7
```

Then open `/setup` on the machine's address and enter the token the script printed. See
[Installation](docs/user/installation.md).

## License

[MIT](LICENSE)

## Development

Requires the .NET 10 SDK.

```sh
dotnet tool restore
dotnet format --verify-no-changes   # style and analyzers
dotnet build -c Release             # warnings are errors
dotnet test -c Release              # fails below 75 % line coverage per project
```

Coverage reports land in `artifacts/coverage/`. CI runs the same steps plus a secret scan, a
dependency license check and a Conventional Commits check on the PR title.

Documentation: <https://bexy315.github.io/autocanvas/> — sources in `docs/`, site in `docs-site/`
(`npm ci && npm run start` for a local preview).
