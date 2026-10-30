# Report fixtures

Real report files from the tools AutoCanvas supports, used by the reader tests. Each sample run
mixes passing, failing, erroring and skipped tests on purpose.

Generated files were produced on 2026-10-30 and then cleaned: machine names, user names and absolute
paths were replaced (`ci-host`, `runner`, `/work/…`). Nothing else was edited.

| File | Source |
|---|---|
| `trx/xunit-v3-mtp.trx` | Generated: xUnit v3 4.0.2 on Microsoft.Testing.Platform, `dotnet test --report-trx` (.NET 10). Has a theory with three data rows and a skipped test whose reason is in `StdOut`. |
| `junit/pytest.xml` | Generated: pytest with `-o junit_family=xunit2 --junitxml`. Has a `failure` and an `error` (missing fixture). |
| `junit/jest-junit.xml` | Generated: Jest 30 with jest-junit 16. Failures carry no `message` attribute, only text. |
| `junit/go-junit-report.xml` | Generated: `go test -v` piped through go-junit-report v2.1.0. Subtests, CDATA output. |
| `junit/maven-surefire.xml` | Hand-written after the Surefire 3 report schema (no Maven available): `flakyFailure` (passed on rerun), `rerunFailure` (failed on every run), `error`, and the thousands separator in `time` that some Surefire versions write. |
| `junit/gradle.xml` | Hand-written after Gradle's JUnit XML output: single `testsuite` root, suite-level `system-out`. |
