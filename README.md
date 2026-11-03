# AutoCanvas

Self-hosted automation and CI/CD engine for developers and technicians: pipelines as YAML in your
repository or built visually on a canvas, executed by lightweight runners, with test reports as a
first-class feature.

> **Status: pre-alpha.** No release yet. The current milestone is
> [S4 — Setup and packaging](docs/plans/s4.md).

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
