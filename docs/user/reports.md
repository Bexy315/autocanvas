---
sidebar_position: 5
---

# Test and coverage reports

A job can publish the test results and code coverage its steps produce. AutoCanvas reads them after the job's steps —
also when a step failed, which is exactly when they matter — and shows every test of the run with
its outcome, duration, message and output, and how much of the code the tests reached.

:::note Status

The server stores the reports of every job; tests, coverage and the history of a test are shown
on a run's page in the web UI (see [In the web UI](#in-the-web-ui)) and read through the API.

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

## In the web UI

A run's page has three views: **Jobs**, **Tests** and **Coverage**. The tabs already say what the
reports found ("3 failed", "82 %"), and each job names its own results with a link to them.

- **Tests**:
  - **Filters**: by outcome; the failed ones come first when there are any. A name search narrows
    them further.
  - **Reports**: one section per test report, with its totals. Reports of earlier attempts of a
    retried job are kept apart, as they do not count in the totals.
  - **Failed tests**: each shows its message, stack trace and output, each of them copyable.
- **History** (the clock at a test): the test's newest results in the automation's runs, as a strip
  of colored squares (oldest left, each a link to its run), with its durations and its failures
  grouped by message. The test **looks flaky** when it failed and then passed on a retry in the same
  run, or when it changed between passing and failing at least three times in its last 20 results
  (skipped ones do not count).
- **Coverage**:
  - the run's line and branch coverage;
  - each coverage report's packages, lowest coverage first (or by name);
  - a package's classes, with the lines no test reached, by number (AutoCanvas keeps no source
    code).

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
`com.example.CartTest.sumsPrices`; that identity is what the [history of a test](#the-history-of-a-test) follows from
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

## Coverage

**Cobertura XML** is the coverage format AutoCanvas reads. Most coverage tools write it directly
or through a converter:

| Tool | Command or setting | Typical `path` |
|---|---|---|
| .NET (coverlet on Microsoft.Testing.Platform) | `dotnet test --coverlet --coverlet-output-format cobertura` (package `coverlet.MTP`) | `**/TestResults/*.cobertura.xml` |
| .NET (VSTest) | `dotnet test --collect "XPlat Code Coverage"` | `**/TestResults/*/coverage.cobertura.xml` |
| Python | `pytest --cov --cov-report=xml` or `coverage xml` | `coverage.xml` |
| Jest | `coverageReporters: ["cobertura"]` | `coverage/cobertura-coverage.xml` |
| Java (JaCoCo) | convert JaCoCo XML, for example with `cover2cover` | as written by the converter |
| Go | `go test -coverprofile=cover.out` and `gocover-cobertura < cover.out > coverage.xml` | `coverage.xml` |

```yaml
jobs:
  test:
    image: python:3.13
    steps:
      - run: pip install -r requirements.txt pytest pytest-cov
      - run: pytest --junitxml=reports/junit.xml --cov --cov-branch --cov-report=xml
    reports:
      - format: junit
        path: reports/junit.xml
      - format: cobertura
        path: coverage.xml
```

AutoCanvas shows coverage per package (an assembly in .NET, a directory in Python or JavaScript)
and per class or file, with the share of lines and branches covered and the lines no test reached,
such as `10-11, 19, 45`. A few details:

- Totals are computed from the lines in the report, not taken from the rates a tool writes on top,
  which are rounded.
- Classes the compiler generates for async methods, iterators and lambdas (`Foo/<Bar>d__3`,
  `Foo+<>c`) and nested classes count towards the class they belong to.
- Lines that tools repeat under each method are counted once.
- Files without coverable lines, such as an empty `__init__.py`, are left out.

## Reading reports

After the steps, the runner uploads every file that matches a report's `path`. The server reads each
file while it arrives and keeps what it found — the file itself is not kept. Texts are masked with
the run's secrets like the log. Reports never change a job's result: a failing test usually fails
its step already, and a file that cannot be read shows why on the run.

Reports are read with Viewer, like the run:

- `GET /api/v1/runs/{id}/reports` lists every report of the run — per job and attempt, with its
  counts or coverage totals, or the reason it could not be read — and totals over the run. A retried
  job counts once in the totals, with the latest attempt that uploaded reports.
- `GET /api/v1/runs/{id}/jobs/{key}` shows the same summaries with each attempt of the job.
- `GET /api/v1/runs/{id}/reports/{reportId}` shows one report and, for coverage, its packages, each
  with an `index`.
- `GET /api/v1/runs/{id}/reports/{reportId}/tests` lists its tests in the order of the file, with
  outcome, duration, message, stack trace and output. `outcome=failed` (or `passed`, `skipped`)
  keeps only those; read on with `cursor` set to `next`.
- `GET /api/v1/runs/{id}/reports/{reportId}/coverage/classes` lists its classes with the lines no
  test reached, such as `10-11,19,45`; `package=<index>` keeps the classes of one package.

```json
{
  "tests": { "total": 412, "passed": 409, "failed": 2, "skipped": 1, "durationMs": 81234 },
  "coverage": { "linesCovered": 5120, "linesValid": 6200, "branchesCovered": 900, "branchesValid": 1300, "lineRate": 0.8258, "branchRate": 0.6923 },
  "reports": [
    { "jobKey": "test", "name": "Unit tests", "format": "junit", "kind": "Tests", "path": "reports/junit.xml", "tests": { "total": 412, "passed": 409, "failed": 2, "skipped": 1, "durationMs": 81234 }, "error": null, "warnings": [], "truncated": false }
  ]
}
```

## The history of a test

`GET /api/v1/automations/{id}/tests/history?name=com.example.CartTest.sumsPrices` returns the newest
results of a test in the automation's runs — run number, job, attempt, outcome, duration and
message — and how often it passed, failed and was skipped among them. `limit` sets how many results
(50 by default, at most 200). A test that passes in some runs and fails in others without a change
is a flaky test; its history shows it.

```json
{
  "fullName": "com.example.CartTest.sumsPrices",
  "passed": 18, "failed": 2, "skipped": 0,
  "entries": [
    { "runNumber": 41, "jobKey": "test", "attempt": 1, "outcome": "Passed", "durationMs": 12, "message": null, "reportedAt": "2026-11-01T10:00:00Z" }
  ]
}
```

The history follows the full name exactly: a renamed test starts a new history.

## Limits

A report file keeps the details of at most 100,000 tests, a coverage file of at most 50,000 classes;
the counts and totals still include everything, and the run shows a warning. Long texts are shortened: names to 1,000 characters, messages to 4,000,
stack traces to 8,000 and output to 4,000.

A job uploads at most `Reports:MaxFilesPerJob` files (100 by default), each at most
`Reports:MaxBytesPerFile` (50 MiB by default); see [Operations](../technical/operations.md).

A file that is not a report of its declared format — not XML, or a different root element — is
reported on the run with the reason; it does not fail the job.
