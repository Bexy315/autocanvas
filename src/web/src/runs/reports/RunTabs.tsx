/**
 * The views of a run: its jobs (graph, job and log), its tests and its coverage. The view is part of
 * the address (`?view=tests`), so a link to a run's failed tests shows them.
 */
import { clsx } from 'clsx';
import { useState } from 'react';
import { Link } from 'react-router';
import type { RunReports, TestCase } from '../../api/reports';
import { ErrorAlert } from '../../ui/Alert';
import { EmptyState } from '../../ui/EmptyState';
import { Loading } from '../../ui/Spinner';
import { CoverageView } from './CoverageView';
import { percent, type RunViewName } from './reportModel';
import { TestHistoryPanel } from './TestHistoryPanel';
import { TestsView } from './TestsView';
import { FlaskConical } from 'lucide-react';

export function RunTabs({ view, reports }: { view: RunViewName; reports: RunReports | undefined }) {
  const tests = reports?.tests;
  const failed = Number(tests?.failed ?? 0);
  const tabs: { name: RunViewName; label: string; badge: string | null; tone: 'danger' | 'muted' }[] = [
    { name: 'jobs', label: 'Jobs', badge: null, tone: 'muted' },
    {
      name: 'tests',
      label: 'Tests',
      badge: tests ? (failed > 0 ? `${failed} failed` : `${Number(tests.passed)} passed`) : null,
      tone: failed > 0 ? 'danger' : 'muted',
    },
    {
      name: 'coverage',
      label: 'Coverage',
      badge: reports?.coverage ? percent(reports.coverage.lineRate) : null,
      tone: 'muted',
    },
  ];
  return (
    <nav aria-label="Views of the run" className="flex shrink-0 gap-1 border-b border-line bg-surface px-3">
      {tabs.map((tab) => (
        <Link
          key={tab.name}
          to={`?view=${tab.name}`}
          replace
          aria-current={view === tab.name ? 'page' : undefined}
          className={clsx(
            '-mb-px flex items-center gap-1.5 border-b-2 px-2.5 py-2 text-sm',
            view === tab.name ? 'border-accent font-medium text-ink' : 'border-transparent text-muted hover:text-ink',
          )}
        >
          {tab.label}
          {tab.badge && ' '}
          {tab.badge && (
            <span className={clsx('text-xs tabular-nums', tab.tone === 'danger' ? 'text-danger' : 'text-muted')}>
              {tab.badge}
            </span>
          )}
        </Link>
      ))}
    </nav>
  );
}

/** The run's tests or coverage, or why there is nothing to show. */
export function ReportsMain({
  view,
  runId,
  automationId,
  reports,
  error,
}: {
  view: 'tests' | 'coverage';
  runId: string;
  automationId: string | null;
  reports: RunReports | undefined;
  error: unknown;
}) {
  const [history, setHistory] = useState<TestCase | null>(null);
  if (error) {
    return (
      <div className="p-4">
        <ErrorAlert error={error} title="The reports could not be loaded" />
      </div>
    );
  }
  if (!reports) {
    return <Loading label="Loading the reports…" />;
  }
  const kind = view === 'tests' ? 'Tests' : 'Coverage';
  if (!reports.reports.some((report) => report.kind === kind)) {
    return (
      <div className="mx-auto w-full max-w-3xl p-6">
        <EmptyState Icon={FlaskConical} title={view === 'tests' ? 'No test reports' : 'No coverage reports'}>
          A job collects them with <code className="font-mono">reports:</code>, for example{' '}
          <code className="font-mono">{`{format: ${view === 'tests' ? 'junit' : 'cobertura'}, path: …}`}</code>; see
          Reports in the documentation.
        </EmptyState>
      </div>
    );
  }
  return (
    <main className="flex min-h-0 flex-1">
      <div className="min-w-0 flex-1 overflow-y-auto">
        {view === 'tests' ? (
          <TestsView runId={runId} reports={reports} onHistory={automationId ? setHistory : () => {}} />
        ) : (
          <CoverageView runId={runId} reports={reports} />
        )}
      </div>
      {view === 'tests' && history && automationId && (
        <TestHistoryPanel
          key={history.fullName}
          automationId={automationId}
          test={history}
          runId={runId}
          onClose={() => setHistory(null)}
        />
      )}
    </main>
  );
}
