/** What the report views compute from the server's answers, without React. */
import type { CoverageTotals, ReportSummary, TestHistoryEntry, TestTotals } from '../../api/reports';

/** A rate from 0 to 1 as a whole percentage, or `–` when there was nothing to measure. */
export function percent(rate: number | string | null | undefined): string {
  if (rate === null || rate === undefined || rate === '') {
    return '–';
  }
  const value = Number(rate) * 100;
  // 99.6 % is not 100 %: only full coverage reads as such.
  return `${value > 99 && value < 100 ? Math.floor(value) : Math.round(value)} %`;
}

export interface LineRange {
  from: number;
  to: number;
}

/** `10-11,19,45` as ranges; parts that are not line numbers are left out. */
export function parseLines(text: string): LineRange[] {
  return text
    .split(',')
    .map((part) => part.trim())
    .flatMap((part) => {
      const match = /^(\d+)(?:-(\d+))?$/.exec(part);
      if (!match) {
        return [];
      }
      const from = Number(match[1]);
      const to = match[2] === undefined ? from : Number(match[2]);
      return [{ from: Math.min(from, to), to: Math.max(from, to) }];
    });
}

export function lineCount(ranges: readonly LineRange[]): number {
  return ranges.reduce((sum, range) => sum + range.to - range.from + 1, 0);
}

export function rangesText(ranges: readonly LineRange[]): string {
  return ranges.map((range) => (range.from === range.to ? `${range.from}` : `${range.from}–${range.to}`)).join(', ');
}

/** `409 tests, 3 failed, 2 skipped`. */
export function testTotalsText(totals: TestTotals): string {
  const total = Number(totals.total);
  const parts = [`${total} ${total === 1 ? 'test' : 'tests'}`];
  if (Number(totals.failed) > 0) {
    parts.push(`${Number(totals.failed)} failed`);
  }
  if (Number(totals.skipped) > 0) {
    parts.push(`${Number(totals.skipped)} skipped`);
  }
  return parts.join(', ');
}

/** `82 % of lines, 64 % of branches`. */
export function coverageText(totals: CoverageTotals): string {
  const branches = Number(totals.branchesValid) > 0 ? `, ${percent(totals.branchRate)} of branches` : '';
  return `${percent(totals.lineRate)} of lines${branches}`;
}

/**
 * The reports that count: of each job, those of its latest attempt that uploaded any, as the
 * server's totals count them. Reports of earlier attempts are kept apart.
 */
export function latestReports(reports: readonly ReportSummary[]): {
  current: ReportSummary[];
  earlier: ReportSummary[];
} {
  const latest = new Map<string, string>();
  for (const report of reports) {
    // Reports come by job and then as received: the last attempt of a job seen wins.
    latest.set(report.jobKey, report.attemptId);
  }
  return {
    current: reports.filter((report) => latest.get(report.jobKey) === report.attemptId),
    earlier: reports.filter((report) => latest.get(report.jobKey) !== report.attemptId),
  };
}

/** How many of the newest results the flaky rule looks at. */
export const flakyWindow = 20;

export interface Flakiness {
  flaky: boolean;
  /** Why, in words; empty when it is not flaky. */
  reason: string;
}

/**
 * Whether a test looks flaky: it failed and then passed again within one run (on a retry of its
 * job), or among its newest results it went from passing to failing and back at least twice. A test
 * that fails steadily, or passes steadily, is not flaky; skipped results are ignored.
 */
export function flakiness(entries: readonly TestHistoryEntry[]): Flakiness {
  const ordered = entries
    .filter((entry) => entry.outcome !== 'Skipped')
    .toSorted((a, b) => Date.parse(a.reportedAt) - Date.parse(b.reportedAt));

  const byRun = new Map<string, TestHistoryEntry[]>();
  for (const entry of ordered) {
    byRun.set(entry.runId, [...(byRun.get(entry.runId) ?? []), entry]);
  }
  for (const results of [...byRun.values()].toReversed()) {
    const sorted = results.toSorted((a, b) => Number(a.attempt) - Number(b.attempt));
    const failedAt = sorted.findIndex((entry) => entry.outcome === 'Failed');
    if (failedAt !== -1 && sorted.slice(failedAt + 1).some((entry) => entry.outcome === 'Passed')) {
      return { flaky: true, reason: `It failed and then passed on a retry in run #${sorted[0]?.runNumber}.` };
    }
  }

  const recent = ordered.slice(-flakyWindow);
  let flips = 0;
  for (let at = 1; at < recent.length; at += 1) {
    if (recent[at]?.outcome !== recent[at - 1]?.outcome) {
      flips += 1;
    }
  }
  return flips >= 3
    ? {
        flaky: true,
        reason: `It changed between passing and failing ${flips} times in its last ${recent.length} results.`,
      }
    : { flaky: false, reason: '' };
}

/** The failures of a test grouped by message, most frequent first. */
export function failureGroups(entries: readonly TestHistoryEntry[]): { message: string; count: number }[] {
  const counts = new Map<string, number>();
  for (const entry of entries) {
    if (entry.outcome === 'Failed') {
      const message = (entry.message ?? 'No message').split('\n')[0] ?? '';
      counts.set(message, (counts.get(message) ?? 0) + 1);
    }
  }
  return [...counts.entries()].map(([message, count]) => ({ message, count })).toSorted((a, b) => b.count - a.count);
}

/** The median of some durations in milliseconds. */
export function median(values: readonly number[]): number {
  if (values.length === 0) {
    return 0;
  }
  const sorted = values.toSorted((a, b) => a - b);
  const middle = Math.floor(sorted.length / 2);
  return sorted.length % 2 === 1 ? (sorted[middle] ?? 0) : ((sorted[middle - 1] ?? 0) + (sorted[middle] ?? 0)) / 2;
}

/** A test's duration: `850 ms`, `1.2 s`, `2 min 5 s`. */
export function testDuration(milliseconds: number | string): string {
  const value = Number(milliseconds);
  if (value < 1000) {
    return `${value} ms`;
  }
  if (value < 60_000) {
    return `${(value / 1000).toFixed(1)} s`;
  }
  return `${Math.floor(value / 60_000)} min ${Math.round((value % 60_000) / 1000)} s`;
}

/** The views of a run page, as `?view=` names them. */
export type RunViewName = 'jobs' | 'tests' | 'coverage';

export function viewOf(value: string | null): RunViewName {
  return value === 'tests' || value === 'coverage' ? value : 'jobs';
}
