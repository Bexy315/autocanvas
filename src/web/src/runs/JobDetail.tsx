import { clsx } from 'clsx';
import { Link } from 'react-router';
import { useRunReports, type ReportSummary } from '../api/reports';
import { useRunJob } from '../api/runs';
import { duration } from '../lib/time';
import { ErrorAlert } from '../ui/Alert';
import { Badge, type BadgeTone } from '../ui/Badge';
import { Loading } from '../ui/Spinner';
import { LogView } from './log/LogView';
import { coverageText, latestReports, testTotalsText } from './reports/reportModel';
import { useAttemptLog } from './log/useAttemptLog';
import { useNow } from './useNow';

const jobTones: Record<string, BadgeTone> = {
  Pending: 'neutral',
  Running: 'accent',
  Succeeded: 'ok',
  Failed: 'danger',
  Cancelled: 'neutral',
  Skipped: 'neutral',
};

/**
 * A job of a run: its status, runner, reports and outputs in one block, its attempts (the last one
 * shown first), and the attempt's log, which lists its steps.
 */
export function JobDetail({
  runId,
  jobKey,
  attemptNumber,
  onAttempt,
}: {
  runId: string;
  jobKey: string;
  /** The attempt to show; the last one when null. */
  attemptNumber: number | null;
  onAttempt: (number: number) => void;
}) {
  const job = useRunJob(runId, jobKey);
  const attempts = job.data?.attempts ?? [];
  const attempt = attempts.find((candidate) => Number(candidate.number) === attemptNumber) ?? attempts.at(-1) ?? null;
  const log = useAttemptLog(runId, attempt);
  const now = useNow(job.data?.status === 'Running');
  const reports = useRunReports(runId, job.data?.status === 'Running');

  if (job.isError) {
    return <ErrorAlert error={job.error} title="The job could not be loaded" />;
  }
  if (!job.data) {
    return <Loading label="Loading the job…" />;
  }
  const detail = job.data;
  const outputs = Object.entries(detail.outputs ?? {});

  return (
    <div className="flex h-full min-h-0 flex-col">
      <header className="flex flex-col gap-1.5 border-b border-line bg-surface px-4 py-2.5">
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="text-base font-semibold">{detail.name}</h2>
          <Badge tone={jobTones[detail.status] ?? 'neutral'}>{detail.status}</Badge>
          <span className="text-xs text-muted">
            {[
              detail.startedAt ? duration(detail.startedAt, detail.finishedAt, now) : null,
              attempt ? (attempt.runnerName ? `on ${attempt.runnerName}` : 'waiting for a runner') : null,
            ]
              .filter(Boolean)
              .join(' · ')}
          </span>
          <JobReports reports={reports.data?.reports ?? []} jobKey={jobKey} />
        </div>
        {detail.reason && <p className="text-sm text-muted">{detail.reason}</p>}
        {outputs.length > 0 && (
          <dl className="flex flex-wrap gap-x-4 gap-y-1 text-xs">
            {outputs.map(([name, value]) => (
              <div key={name} className="flex gap-1.5">
                <dt className="text-muted">{name}</dt>
                <dd className="font-mono">{value === '***' ? <span className="text-muted">hidden</span> : value}</dd>
              </div>
            ))}
          </dl>
        )}
        {attempts.length > 1 && (
          <fieldset className="flex flex-wrap gap-1">
            <legend className="sr-only">Attempt</legend>
            {attempts.map((candidate) => (
              <button
                key={candidate.id}
                type="button"
                aria-pressed={candidate.id === attempt?.id}
                onClick={() => onAttempt(Number(candidate.number))}
                className={clsx(
                  'rounded-control border px-2 py-0.5 text-xs',
                  candidate.id === attempt?.id
                    ? 'border-accent bg-accent-soft text-accent'
                    : 'border-line-strong text-muted hover:text-ink',
                )}
              >
                Attempt {candidate.number} · {candidate.status}
              </button>
            ))}
          </fieldset>
        )}
      </header>

      {attempt === null ? (
        <p className="p-4 text-sm text-muted">
          {detail.status === 'Skipped' ? 'The job was skipped, so it never ran.' : 'The job has not started yet.'}
        </p>
      ) : (
        <>
          {attempt.error && (
            <p className="border-b border-line bg-danger-soft px-4 py-2 text-sm text-danger">{attempt.error}</p>
          )}
          <LogView
            key={attempt.id}
            log={log}
            steps={attempt.steps}
            label={`${detail.name}, attempt ${attempt.number}`}
          />
        </>
      )}
    </div>
  );
}

/** What the job's reports found, in a line that leads to the run's tests or coverage. */
function JobReports({ reports, jobKey }: { reports: readonly ReportSummary[]; jobKey: string }) {
  const own = latestReports(reports).current.filter((report) => report.jobKey === jobKey);
  const tests = own.flatMap((report) => (report.tests ? [report.tests] : []));
  const coverage = own.find((report) => report.coverage)?.coverage;
  if (tests.length === 0 && !coverage) {
    return null;
  }
  const sum = (key: 'total' | 'passed' | 'failed' | 'skipped' | 'durationMs') =>
    tests.reduce((total, entry) => total + Number(entry[key]), 0);
  const failed = sum('failed');
  return (
    <span className="flex flex-wrap gap-x-3 text-xs">
      {tests.length > 0 && (
        <Link to="?view=tests" className={failed > 0 ? 'text-danger hover:underline' : 'text-muted hover:underline'}>
          {testTotalsText({
            total: sum('total'),
            passed: sum('passed'),
            failed,
            skipped: sum('skipped'),
            durationMs: sum('durationMs'),
          })}
        </Link>
      )}
      {coverage && (
        <Link to="?view=coverage" className="text-muted hover:underline">
          Coverage: {coverageText(coverage)}
        </Link>
      )}
    </span>
  );
}
