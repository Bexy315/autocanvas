/**
 * The tests of a run: one section per test report, filtered by outcome (failures first when there
 * are any), each test with its message, stack trace and output, and the way to its history.
 */
import { clsx } from 'clsx';
import { ChevronDown, ChevronRight, CircleCheck, CircleSlash, CircleX, Copy, History, Search } from 'lucide-react';
import { useState } from 'react';
import {
  useReportTests,
  type ReportSummary,
  type RunReports,
  type TestCase,
  type TestOutcome,
} from '../../api/reports';
import { ErrorAlert } from '../../ui/Alert';
import { Button } from '../../ui/Button';
import { Loading } from '../../ui/Spinner';
import { latestReports, testDuration, testTotalsText } from './reportModel';

const outcomeIcons: Record<TestOutcome, { Icon: typeof CircleCheck; className: string }> = {
  Passed: { Icon: CircleCheck, className: 'text-ok' },
  Failed: { Icon: CircleX, className: 'text-danger' },
  Skipped: { Icon: CircleSlash, className: 'text-muted' },
};

export function TestsView({
  runId,
  reports,
  onHistory,
}: {
  runId: string;
  reports: RunReports;
  onHistory: (test: TestCase) => void;
}) {
  const totals = reports.tests;
  const failed = Number(totals?.failed ?? 0);
  const [outcome, setOutcome] = useState<TestOutcome | null>(failed > 0 ? 'Failed' : null);
  const [query, setQuery] = useState('');
  const testReports = reports.reports.filter((report) => report.kind === 'Tests');
  const { current, earlier } = latestReports(testReports);
  const filters: { value: TestOutcome | null; label: string; count: number | null }[] = [
    { value: 'Failed', label: 'Failed', count: failed },
    { value: 'Passed', label: 'Passed', count: Number(totals?.passed ?? 0) },
    { value: 'Skipped', label: 'Skipped', count: Number(totals?.skipped ?? 0) },
    { value: null, label: 'All', count: Number(totals?.total ?? 0) },
  ];

  return (
    <div className="mx-auto flex w-full max-w-6xl flex-col gap-4 p-4">
      <div className="flex flex-wrap items-center gap-2">
        <fieldset className="flex flex-wrap gap-1">
          <legend className="sr-only">Show tests that</legend>
          {filters.map((filter) => (
            <button
              key={filter.label}
              type="button"
              aria-pressed={outcome === filter.value}
              onClick={() => setOutcome(filter.value)}
              className="rounded-control border border-line-strong px-2.5 py-1 text-sm text-muted hover:text-ink aria-pressed:border-accent aria-pressed:bg-accent-soft aria-pressed:text-accent"
            >
              {filter.label} <span className="tabular-nums">{filter.count}</span>
            </button>
          ))}
        </fieldset>
        <label className="ml-auto flex min-w-56 items-center gap-1.5 rounded-control border border-line-strong bg-surface px-2">
          <Search aria-hidden="true" size={13} className="text-muted" />
          <span className="sr-only">Find a test by name</span>
          <input
            type="search"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="Find a test by name"
            className="h-8 min-w-0 flex-1 bg-transparent text-sm outline-none placeholder:text-muted"
          />
        </label>
      </div>
      {current.map((report) => (
        <ReportSection
          key={report.id}
          runId={runId}
          report={report}
          outcome={outcome}
          query={query}
          onHistory={onHistory}
        />
      ))}
      {earlier.length > 0 && (
        <details className="rounded-panel border border-line bg-surface">
          <summary className="cursor-pointer px-4 py-2.5 text-sm text-muted">
            Reports of earlier attempts ({earlier.length}); they do not count in the totals
          </summary>
          <div className="flex flex-col gap-3 p-3 opacity-80">
            {earlier.map((report) => (
              <ReportSection
                key={report.id}
                runId={runId}
                report={report}
                outcome={outcome}
                query={query}
                onHistory={onHistory}
              />
            ))}
          </div>
        </details>
      )}
    </div>
  );
}

function ReportSection({
  runId,
  report,
  outcome,
  query,
  onHistory,
}: {
  runId: string;
  report: ReportSummary;
  outcome: TestOutcome | null;
  query: string;
  onHistory: (test: TestCase) => void;
}) {
  const [open, setOpen] = useState(true);
  const tests = useReportTests(runId, report.id, outcome, open && report.error === null);
  const needle = query.trim().toLowerCase();
  const items = (tests.data?.pages.flatMap((page) => page.items) ?? []).filter(
    (test) => needle === '' || test.fullName.toLowerCase().includes(needle),
  );
  const headingId = `report-${report.id}`;

  return (
    <section aria-labelledby={headingId} className="rounded-panel border border-line bg-surface">
      <h3 id={headingId}>
        <button
          type="button"
          aria-expanded={open}
          onClick={() => setOpen(!open)}
          className="flex w-full flex-wrap items-center gap-x-3 gap-y-1 px-4 py-2.5 text-left hover:bg-sunken"
        >
          {open ? <ChevronDown aria-hidden="true" size={14} /> : <ChevronRight aria-hidden="true" size={14} />}
          <span className="font-semibold">{report.name}</span>
          <span className="text-xs text-muted">
            {report.jobKey} · {report.format}
          </span>
          {report.tests && (
            <span className={clsx('ml-auto text-sm', Number(report.tests.failed) > 0 ? 'text-danger' : 'text-muted')}>
              {testTotalsText(report.tests)} · {testDuration(report.tests.durationMs)}
            </span>
          )}
        </button>
      </h3>
      {open && (
        <div className="border-t border-line">
          {report.error && <p className="px-4 py-2 text-sm text-danger">The file could not be read: {report.error}</p>}
          {report.warnings.length > 0 && (
            <ul className="bg-warn-soft px-4 py-2 text-xs text-ink">
              {report.warnings.map((warning) => (
                <li key={warning}>{warning}</li>
              ))}
            </ul>
          )}
          {report.truncated && (
            <p className="bg-warn-soft px-4 py-2 text-xs text-ink">
              Only part of the details was kept; the totals count every test.
            </p>
          )}
          {tests.isPending && report.error === null && <Loading label="Loading tests…" />}
          {tests.isError && <ErrorAlert error={tests.error} title="The tests could not be loaded" />}
          {tests.data && items.length === 0 && (
            <p className="px-4 py-3 text-sm text-muted">
              {needle !== '' ? 'No test of this report matches.' : `No ${outcome ? outcome.toLowerCase() : ''} tests.`}
            </p>
          )}
          {items.length > 0 && (
            <ul aria-label={`Tests of ${report.name}`} className="divide-y divide-line">
              {items.map((test) => (
                <TestRow key={test.index} test={test} onHistory={() => onHistory(test)} />
              ))}
            </ul>
          )}
          {tests.hasNextPage && (
            <div className="flex justify-center border-t border-line p-2">
              <Button busy={tests.isFetchingNextPage} onClick={() => void tests.fetchNextPage()}>
                Load more tests
              </Button>
            </div>
          )}
        </div>
      )}
    </section>
  );
}

function TestRow({ test, onHistory }: { test: TestCase; onHistory: () => void }) {
  const [open, setOpen] = useState(test.outcome === 'Failed');
  const { Icon, className } = outcomeIcons[test.outcome];
  const hasDetails = Boolean(test.message || test.stackTrace || test.output);
  return (
    <li>
      <div className="flex items-center gap-2 px-4 py-1.5">
        <Icon aria-label={test.outcome} size={14} className={clsx('shrink-0', className)} />
        <button
          type="button"
          aria-expanded={hasDetails ? open : undefined}
          disabled={!hasDetails}
          onClick={() => setOpen(!open)}
          className="flex min-w-0 flex-1 items-baseline gap-2 text-left disabled:cursor-default"
        >
          <span className="truncate text-xs text-muted">{test.suite}</span>
          <span className="truncate text-sm">{test.name}</span>
        </button>
        <span className="shrink-0 text-xs text-muted tabular-nums">{testDuration(test.durationMs)}</span>
        <button
          type="button"
          onClick={onHistory}
          aria-label={`History of ${test.fullName}`}
          title="History"
          className="shrink-0 rounded-control p-1 text-muted hover:bg-sunken hover:text-ink"
        >
          <History aria-hidden="true" size={14} />
        </button>
      </div>
      {open && hasDetails && (
        <div className="flex flex-col gap-2 px-4 pb-3 pl-10">
          {test.message && <p className="text-sm whitespace-pre-wrap text-ink">{test.message}</p>}
          {test.stackTrace && <Block title="Stack trace" text={test.stackTrace} />}
          {test.output && <Block title="Output" text={test.output} />}
        </div>
      )}
    </li>
  );
}

/** Text in monospace that folds away and can be copied. */
function Block({ title, text }: { title: string; text: string }) {
  return (
    <details open={title === 'Stack trace'} className="rounded-control border border-line">
      <summary className="flex cursor-pointer items-center justify-between gap-2 px-2 py-1 text-xs text-muted">
        {title}
        <button
          type="button"
          aria-label={`Copy the ${title.toLowerCase()}`}
          title="Copy"
          onClick={(event) => {
            event.preventDefault();
            void navigator.clipboard?.writeText(text);
          }}
          className="rounded p-0.5 hover:bg-sunken hover:text-ink"
        >
          <Copy aria-hidden="true" size={12} />
        </button>
      </summary>
      <pre className="max-h-72 overflow-auto bg-sunken px-2 py-1.5 font-mono text-xs leading-5">{text}</pre>
    </details>
  );
}
