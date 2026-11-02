import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ReportSummary, TestCase, TestHistoryEntry } from '../../api/reports';
import type { RunView } from '../../api/runs';
import { automationView, folderView, page, twoJobs } from '../../test/fixtures';
import { fakeServer, info, json, me, problem, requests, type Handler } from '../../test/fakeServer';
import { withLayout } from '../../test/layout';
import { renderApp } from '../../test/renderApp';
import {
  coverageText,
  failureGroups,
  flakiness,
  latestReports,
  lineCount,
  median,
  parseLines,
  percent,
  rangesText,
  testDuration,
  testTotalsText,
  viewOf,
} from './reportModel';

function entry(number: number, outcome: TestHistoryEntry['outcome'], attempt = 1, message: string | null = null) {
  return {
    runId: `r${number}`,
    runNumber: number,
    jobKey: 'build',
    attemptId: `t${number}-${attempt}`,
    attempt,
    reportId: `x${number}`,
    outcome,
    durationMs: 10 * number,
    message,
    reportedAt: new Date(Date.UTC(2026, 9, number, attempt)).toISOString(),
  } satisfies TestHistoryEntry;
}

const uploaded = (id: string, job: string, attempt: string) =>
  ({ id, jobKey: job, attemptId: attempt }) as ReportSummary;

describe('the report model', () => {
  it('writes rates, lines, totals and durations', () => {
    expect([percent(0.82), percent(0.996), percent(1), percent(null), percent('0.5')]).toEqual([
      '82 %',
      '99 %',
      '100 %',
      '–',
      '50 %',
    ]);
    const ranges = parseLines('10-11, 19,45-44,x');
    expect(ranges).toEqual([
      { from: 10, to: 11 },
      { from: 19, to: 19 },
      { from: 44, to: 45 },
    ]);
    expect(lineCount(ranges)).toBe(5);
    expect(rangesText(ranges)).toBe('10–11, 19, 44–45');
    expect(testTotalsText({ total: 1, passed: 1, failed: 0, skipped: 0, durationMs: 1 })).toBe('1 test');
    expect(testTotalsText({ total: 9, passed: 5, failed: 3, skipped: 1, durationMs: 1 })).toBe(
      '9 tests, 3 failed, 1 skipped',
    );
    const totals = {
      linesCovered: 1,
      linesValid: 2,
      branchesCovered: 0,
      branchesValid: 0,
      lineRate: 0.5,
      branchRate: null,
    };
    expect(coverageText(totals)).toBe('50 % of lines');
    expect(coverageText({ ...totals, branchesValid: 4, branchRate: 0.25 })).toBe('50 % of lines, 25 % of branches');
    expect([testDuration(850), testDuration(1250), testDuration(125_000)]).toEqual(['850 ms', '1.3 s', '2 min 5 s']);
    expect([median([]), median([3, 1, 2]), median([4, 1, 2, 3])]).toEqual([0, 2, 2.5]);
    expect([viewOf('tests'), viewOf('coverage'), viewOf(null), viewOf('nonsense')]).toEqual([
      'tests',
      'coverage',
      'jobs',
      'jobs',
    ]);
  });

  it('counts only the latest attempt of each job that uploaded reports', () => {
    const { current, earlier } = latestReports([
      uploaded('a', 'build', 't1'),
      uploaded('b', 'build', 't2'),
      uploaded('c', 'lint', 'l1'),
    ]);
    expect(current.map((found) => found.id)).toEqual(['b', 'c']);
    expect(earlier.map((found) => found.id)).toEqual(['a']);
  });

  it('calls a test flaky when it passes on a retry, or flips between passing and failing', () => {
    expect(flakiness([entry(1, 'Passed'), entry(2, 'Failed'), entry(2, 'Passed', 2)])).toEqual({
      flaky: true,
      reason: 'It failed and then passed on a retry in run #2.',
    });
    const flips = [entry(1, 'Passed'), entry(2, 'Failed'), entry(3, 'Passed'), entry(4, 'Skipped'), entry(5, 'Failed')];
    expect(flakiness(flips)).toEqual({
      flaky: true,
      reason: 'It changed between passing and failing 3 times in its last 4 results.',
    });
    expect(flakiness([entry(1, 'Passed'), entry(2, 'Failed'), entry(3, 'Failed'), entry(4, 'Failed')]).flaky).toBe(
      false,
    );
    expect(flakiness([]).flaky).toBe(false);
  });

  it('groups failures by the first line of their message', () => {
    const entries = [
      entry(1, 'Failed', 1, 'Expected 90\nbut 100'),
      entry(2, 'Failed', 1, 'Expected 90\nbut 101'),
      entry(3, 'Failed', 1, null),
      entry(4, 'Passed'),
    ];
    expect(failureGroups(entries)).toEqual([
      { message: 'Expected 90', count: 2 },
      { message: 'No message', count: 1 },
    ]);
  });
});

withLayout();

const team = folderView('team', 'Team');

const run: RunView = {
  id: 'r1',
  number: 1,
  automationId: 'a1',
  automationName: 'build',
  automationVersion: 2,
  folderId: 'team',
  state: 'Failed',
  trigger: 'Manual',
  triggeredByName: 'ada',
  cancelRequested: false,
  cancelledByName: null,
  createdAt: '2026-11-02T10:00:00Z',
  finishedAt: '2026-11-02T10:01:00Z',
  yaml: twoJobs,
  jobs: [
    {
      key: 'build',
      name: 'build',
      status: 'Failed',
      activatedCase: null,
      reason: null,
      attempts: 2,
      startedAt: '2026-11-02T10:00:00Z',
      finishedAt: '2026-11-02T10:01:00Z',
    },
  ],
};

function report(
  id: string,
  kind: ReportSummary['kind'],
  attemptId = 't2',
  extra: Partial<ReportSummary> = {},
): ReportSummary {
  return {
    id,
    jobKey: 'build',
    attemptId,
    name: kind === 'Tests' ? 'unit' : 'coverage',
    format: kind === 'Tests' ? 'junit' : 'cobertura',
    kind,
    path: 'x',
    size: 10,
    receivedAt: '2026-11-02T10:01:00Z',
    tests: kind === 'Tests' ? { total: 3, passed: 1, failed: 1, skipped: 1, durationMs: 30 } : null,
    coverage:
      kind === 'Coverage'
        ? { linesCovered: 8, linesValid: 10, branchesCovered: 1, branchesValid: 4, lineRate: 0.8, branchRate: 0.25 }
        : null,
    error: null,
    warnings: [],
    truncated: false,
    ...extra,
  };
}

function testCase(index: number, outcome: TestCase['outcome'], name: string): TestCase {
  return {
    index,
    group: null,
    suite: 'Cart',
    name,
    fullName: `Cart.${name}`,
    outcome,
    durationMs: 5,
    message: outcome === 'Failed' ? 'Expected 90' : null,
    stackTrace: outcome === 'Failed' ? 'at Cart.cs:42' : null,
    output: outcome === 'Failed' ? 'discount applied' : null,
  };
}

const allTests = [
  testCase(0, 'Passed', 'adds_items'),
  testCase(1, 'Failed', 'applies_discount'),
  testCase(2, 'Skipped', 'later'),
];

function server(extra: Record<string, Handler> = {}) {
  fakeServer({
    'GET /api/v1/info': () => json(info),
    'GET /api/v1/me': () => json(me()),
    'GET /api/v1/folders': () => json([team]),
    'GET /api/v1/automations/a1': () => json(automationView('a1', 'team'), 200, { ETag: '"2"' }),
    'GET /api/v1/runs/r1': () => json(run),
    'GET /api/v1/runs/r1/jobs/build': () => json({ ...run.jobs[0], outputs: {}, attempts: [] }),
    'GET /api/v1/runs/r1/reports': () =>
      json({
        tests: report('t', 'Tests').tests,
        coverage: report('c', 'Coverage').coverage,
        reports: [
          report('old', 'Tests', 't1', { error: 'The file is not JUnit.' }),
          report('t', 'Tests', 't2', { warnings: ['A test had no name.'], truncated: true }),
          report('c', 'Coverage'),
        ],
      }),
    'GET /api/v1/runs/r1/reports/t/tests': (request) => {
      const outcome = new URL(request.url).searchParams.get('outcome');
      const items = allTests.filter((test) => outcome === null || test.outcome.toLowerCase() === outcome);
      return json(page(items, new URL(request.url).searchParams.get('cursor') ? null : outcome ? null : 'more'));
    },
    'GET /api/v1/runs/r1/reports/c': () =>
      json({
        report: report('c', 'Coverage'),
        packages: [
          { index: 0, name: 'Zeta', coverage: { ...report('c', 'Coverage').coverage, lineRate: 0.9 } },
          { index: 1, name: 'Alpha', coverage: { ...report('c', 'Coverage').coverage, lineRate: 0.3 } },
        ],
      }),
    'GET /api/v1/runs/r1/reports/c/coverage/classes': () =>
      json(
        page([
          {
            index: 0,
            package: 1,
            name: 'Alpha.Gate',
            file: 'src/Gate.cs',
            coverage: report('c', 'Coverage').coverage,
            uncovered: '3-5,9',
          },
        ]),
      ),
    'GET /api/v1/automations/a1/tests/history': () =>
      json({
        fullName: 'Cart.applies_discount',
        passed: 2,
        failed: 2,
        skipped: 0,
        entries: [entry(1, 'Passed'), entry(2, 'Failed'), entry(3, 'Passed'), entry(4, 'Failed', 1, 'Expected 90')],
      }),
    ...extra,
  });
}

describe('the reports of a run', () => {
  it('are named in the tabs and in the job, which lead to them', async () => {
    server();
    const { user } = renderApp('/runs/r1');
    const views = await screen.findByRole('navigation', { name: 'Views of the run' });
    expect(await within(views).findByRole('link', { name: 'Tests 1 failed' })).toBeInTheDocument();
    expect(within(views).getByRole('link', { name: 'Coverage 80 %' })).toBeInTheDocument();

    const job = screen.getByRole('region', { name: 'The chosen job' });
    await user.click(await within(job).findByRole('link', { name: '3 tests, 1 failed, 1 skipped' }));
    expect(globalThis.location.search).toBe('?view=tests');
  });

  it('shows the failed tests first, with message, stack trace and output, and every test on request', async () => {
    server();
    const { user } = renderApp('/runs/r1?view=tests');
    const tests = await screen.findByRole('list', { name: 'Tests of unit' });
    expect(within(tests).getByText('applies_discount')).toBeInTheDocument();
    expect(within(tests).queryByText('adds_items')).not.toBeInTheDocument();
    expect(within(tests).getByText('Expected 90')).toBeInTheDocument();
    expect(within(tests).getByText('at Cart.cs:42')).toBeInTheDocument();
    expect(screen.getByText('A test had no name.')).toBeInTheDocument();
    expect(screen.getByText(/Only part of the details was kept/)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'All 3' }));
    expect(
      await within(screen.getByRole('list', { name: 'Tests of unit' })).findByText('adds_items'),
    ).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Load more tests' }));
    await waitFor(() => expect(requests.some((request) => request.url.includes('cursor=more'))).toBe(true));

    await user.type(screen.getByRole('searchbox', { name: 'Find a test by name' }), 'nothing');
    expect(await screen.findByText('No test of this report matches.')).toBeInTheDocument();

    // A report of an earlier attempt is apart, and says why it has no tests.
    await user.click(screen.getByText(/Reports of earlier attempts \(1\)/));
    expect(screen.getByText('The file could not be read: The file is not JUnit.')).toBeInTheDocument();

    await user.click(screen.getAllByRole('button', { name: /^unit/ })[0] ?? document.body);
    expect(screen.queryByRole('list', { name: 'Tests of unit' })).not.toBeInTheDocument();
  });

  it("opens a test's history, which says when it looks flaky, and closes with Esc", async () => {
    server();
    const { user } = renderApp('/runs/r1?view=tests');
    await user.click(await screen.findByRole('button', { name: 'History of Cart.applies_discount' }));
    const panel = await screen.findByRole('complementary', { name: 'History of Cart.applies_discount' });
    expect(await within(panel).findByText('Looks flaky.')).toBeInTheDocument();
    expect(within(panel).getByRole('link', { name: 'Run #4: failed' })).toHaveAttribute('href', '/runs/r4?view=tests');
    const failures = within(panel).getByRole('region', { name: 'Failures' });
    expect(within(failures).getByText('Expected 90')).toBeInTheDocument();
    expect(within(failures).getByText('No message')).toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('complementary')).not.toBeInTheDocument();
  });

  it('shows coverage by package, lowest first or by name, with the lines a class did not reach', async () => {
    server();
    const { user } = renderApp('/runs/r1?view=coverage');
    const packages = await screen.findByRole('list', { name: 'Packages of coverage' });
    expect(
      within(packages)
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual([expect.stringContaining('Alpha'), expect.stringContaining('Zeta')]);
    expect(screen.getByText('8 of 10')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Lowest coverage first' }));
    expect(screen.getByRole('button', { name: 'Sorted by name' })).toBeInTheDocument();
    await user.click(within(packages).getByRole('button', { name: /Alpha/ }));
    expect(await screen.findByText('3–5, 9')).toBeInTheDocument();
    expect(screen.getByText('(4 lines)')).toBeInTheDocument();
  });

  it('says how to get reports when there are none, or why they could not be read', async () => {
    server({ 'GET /api/v1/runs/r1/reports': () => json({ tests: null, coverage: null, reports: [] }) });
    renderApp('/runs/r1?view=coverage');
    expect(await screen.findByRole('heading', { name: 'No coverage reports' })).toBeInTheDocument();

    server({ 'GET /api/v1/runs/r1/reports': () => problem(500, 'Broken.') });
    renderApp('/runs/r1?view=tests');
    expect(await screen.findByText('The reports could not be loaded')).toBeInTheDocument();
  });
});
