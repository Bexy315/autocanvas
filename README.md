# AutoCanvas

Self-hosted automation and CI/CD engine for developers and technicians: pipelines as YAML in your
repository or built visually on a canvas, executed by lightweight runners, with test reports as a
first-class feature.

> **Status: pre-alpha.** Nothing to run yet. The current milestone is
> [S3 — Web-UI and Canvas](docs/plans/s3.md).

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
