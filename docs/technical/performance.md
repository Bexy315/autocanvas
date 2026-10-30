---
sidebar_position: 3
---

# Performance

Performance work in AutoCanvas starts with a number. The benchmarks in
`benchmarks/AutoCanvas.Benchmarks` measure the hot paths of Core and Reports; run them before and after a
change that could affect them, and update the baseline here when the numbers move.

```sh
dotnet run -c Release --project benchmarks/AutoCanvas.Benchmarks -- --filter '*' --job short
```

`--job short` takes about two minutes and is enough to spot regressions; leave it out for precise
numbers. Results land in `BenchmarkDotNet.Artifacts/` (or the folder given with `--artifacts`).

## What is measured

| Benchmark | What it stands for |
|---|---|
| `LoadBenchmarks` | Reading automation files: parsing, validation, both. The sample has 10 or 100 jobs with 10 steps each, services, needs, conditions and expressions throughout (`SampleAutomation`). |
| `ExpressionBenchmarks` | Parsing a condition, evaluating a parsed one, rendering a parsed template, parsing and rendering in one go. |
| `PlannerBenchmarks` | `RunPlanner.Plan` on a run with half of its jobs done — what the server does every time a job finishes. |
| `ReportBenchmarks` | Reading a test report of 10,000 tests, one in ten failing with a 20-line stack trace and output, as JUnit and as TRX; and a Cobertura report of 2,000 classes with 50 lines each. |

## Baseline

Measured on 2026-10-30 with `--job short` on an Intel Core i5-13400F, Windows 11, .NET 10.0.14.

| Benchmark | Size | Mean | Allocated |
|---|---|---:|---:|
| Parse | 10 jobs | 446 µs | 788 KB |
| Parse | 100 jobs | 6.9 ms | 7.9 MB |
| Validate (parsed) | 10 jobs | 63 µs | 246 KB |
| Validate (parsed) | 100 jobs | 728 µs | 2.8 MB |
| Parse and validate | 100 jobs | 7.4 ms | 10.7 MB |
| Parse condition | — | 457 ns | 2.0 KB |
| Evaluate parsed condition | — | 30 ns | 0 B |
| Render parsed template | — | 70 ns | 504 B |
| Parse and render template | — | 414 ns | 1.9 KB |
| Plan a half-done run | 10 jobs | 0.44 µs | 1.6 KB |
| Plan a half-done run | 100 jobs | 8.6 µs | 26 KB |
| Read JUnit report | 10,000 tests | 20 ms | 23 MB |
| Read TRX report | 10,000 tests | 45 ms | 30 MB |
| Read Cobertura report | 2,000 classes, 100,000 lines | 70–90 ms¹ | 49 MB |

¹ Measured on 2026-10-30 in two runs while the machine was busy (JUnit and TRX, unchanged since
the baseline, measured 30–35 ms and 75–80 ms in the same runs). Re-measure on a quiet machine
before comparing.

## Observations

- **Parsing dominates loading.** Validation adds about 10 %. A 100-job automation (1,000 steps)
  loads in about 7 ms, which is fine for saving a file, but the parser allocates about 100 times the
  file size: YamlDotNet builds a full node tree, and every value is copied into the model. If
  loading becomes frequent — for example, discovering automations on every push across many
  repositories — the parser is the first candidate, and a streaming reader could cut the
  allocations.
- **Expressions should be parsed once.** Evaluating a parsed condition is about 15 times faster
  than parsing it and allocates nothing. The server should cache parsed expressions per automation
  version instead of parsing them for every run.
- **Reports read in linear time.** Each test case is materialized on its own, so memory follows the
  number of tests (about 2–3 KB each with failure details), not the file. TRX costs about twice as
  much as JUnit because its results come before the definitions that name their classes and have to
  be kept until the end of the file.
- **Coverage allocates the most per input.** Each class is materialized with all of its lines,
  and every line goes into a dictionary to merge generated classes; about 0.5 KB per line. Fine
  for typical projects; a candidate if very large monorepo reports become common.
- **Planning is cheap.** Deciding the next actions of a 100-job run takes microseconds, far below
  the database work around it.
