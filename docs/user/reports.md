---
sidebar_position: 5
---

# Test reports

A job can publish the test results its steps produce. AutoCanvas reads them after the job's steps —
also when a step failed, which is exactly when they matter — and shows every test of the run with
its outcome, duration, message and output.

:::note Specification

Reading reports is built (`AutoCanvas.Reports`); showing them on a run comes with the server and the
web UI. Until then this page describes formats and configuration.

:::

```yaml
jobs:
  test:
    steps:
      - run: pytest --junitxml=reports/junit.xml
    reports:
      - format: junit
        path: reports/junit.xml
```

| Key | Meaning |
|---|---|
| `format` | `junit` or `trx` for test results; `cobertura` for coverage. |
| `path` | A glob pattern, or a list of them, relative to the workspace. Every matching file is read. |
| `name` | Optional label in the UI; useful when a job publishes several reports. |

## Supported formats

**JUnit XML** is the common format of most test tools. AutoCanvas reads the variants written by
Maven Surefire, Gradle, pytest, jest-junit, go-junit-report and others: a `<testsuites>` or
`<testsuite>` root, nested suites, `failure`, `error` and `skipped`, and test output.

**TRX** is the format of `dotnet test`, for xUnit, NUnit and MSTest.

Every test ends up as one of three outcomes:

| Outcome | JUnit | TRX |
|---|---|---|
| Passed | no `failure`, `error` or `skipped` | `Passed` |
| Failed | `failure` or `error` | `Failed`, `Error`, `Timeout`, `Aborted` and any other outcome |
| Skipped | `skipped` | `NotExecuted`, `NotRunnable`, `Inconclusive`, `Pending`, `Disconnected` |

A test that Maven Surefire rerun until it passed (`flakyFailure`) counts as passed, with a note such
as "Passed after 2 failed attempts." — a hint that the test is flaky.

Each test is identified by its class (or suite) and name, for example
`com.example.CartTest.sumsPrices`; that identity is what the history of a test will follow from
run to run.

## Producing reports

| Tool | Command or setting | Typical `path` |
|---|---|---|
| .NET (Microsoft.Testing.Platform) | `dotnet test --report-trx --results-directory TestResults` (package `Microsoft.Testing.Extensions.TrxReport`) | `TestResults/*.trx` |
| .NET (VSTest) | `dotnet test --logger trx --results-directory TestResults` | `TestResults/*.trx` |
| pytest | `pytest --junitxml=reports/junit.xml` | `reports/junit.xml` |
| Jest | reporter `jest-junit`, output folder via `JEST_JUNIT_OUTPUT_DIR` | `reports/junit.xml` |
| Maven | Surefire writes reports by default | `**/target/surefire-reports/TEST-*.xml` |
| Gradle | the `test` task writes reports by default | `**/build/test-results/test/TEST-*.xml` |
| Go | `go test -v ./... 2>&1 \| go-junit-report -out report.xml` | `report.xml` |

```yaml
jobs:
  dotnet:
    image: mcr.microsoft.com/dotnet/sdk:10.0
    steps:
      - run: dotnet test -c Release --report-trx --results-directory TestResults
    reports:
      - format: trx
        path: 'TestResults/*.trx'

  java:
    image: maven:3-eclipse-temurin-21
    steps:
      - run: mvn -B verify
    reports:
      - format: junit
        path: '**/target/surefire-reports/TEST-*.xml'
        name: Unit tests
```

Quote patterns that start with `*` — in YAML an unquoted `*` starts an alias.

## Limits

A report file keeps the details of at most 100,000 tests; the counts still include every test, and
the run shows a warning. Long texts are shortened: names to 1,000 characters, messages to 4,000,
stack traces to 8,000 and output to 4,000.

A file that is not a report of its declared format — not XML, or a different root element — is
reported on the run with the reason; it does not fail the job.
