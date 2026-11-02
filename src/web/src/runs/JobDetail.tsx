import { clsx } from 'clsx';
import { Ban, CircleCheck, CircleDashed, CircleSlash, CircleX, Clock, LoaderCircle, TriangleAlert } from 'lucide-react';
import { useState } from 'react';
import { useRunJob, type JobStep } from '../api/runs';
import { duration } from '../lib/time';
import { ErrorAlert } from '../ui/Alert';
import { Badge, type BadgeTone } from '../ui/Badge';
import { Loading } from '../ui/Spinner';
import { LogView, type StepJump } from './log/LogView';
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

const stepIcons: Record<JobStep['status'], { Icon: typeof CircleCheck; className: string }> = {
  Sent: { Icon: CircleDashed, className: 'text-muted' },
  Running: { Icon: LoaderCircle, className: 'animate-spin text-accent' },
  Succeeded: { Icon: CircleCheck, className: 'text-ok' },
  Failed: { Icon: CircleX, className: 'text-danger' },
  Cancelled: { Icon: Ban, className: 'text-muted' },
  TimedOut: { Icon: Clock, className: 'text-danger' },
  Errored: { Icon: TriangleAlert, className: 'text-danger' },
  Skipped: { Icon: CircleSlash, className: 'text-muted' },
};

/**
 * A job of a run: its status and outputs, its attempts (the last one shown first), the steps of the
 * attempt shown, and its log.
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
  const [jump, setJump] = useState<StepJump | null>(null);
  const attempts = job.data?.attempts ?? [];
  const attempt = attempts.find((candidate) => Number(candidate.number) === attemptNumber) ?? attempts.at(-1) ?? null;
  const log = useAttemptLog(runId, attempt);
  const now = useNow(job.data?.status === 'Running');

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
      <header className="flex flex-col gap-1.5 border-b border-line bg-surface px-4 py-3">
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="text-base font-semibold">{detail.name}</h2>
          <Badge tone={jobTones[detail.status] ?? 'neutral'}>{detail.status}</Badge>
          {detail.startedAt && (
            <span className="text-xs text-muted">{duration(detail.startedAt, detail.finishedAt, now)}</span>
          )}
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
          <div className="border-b border-line bg-surface px-4 py-2 text-xs text-muted">
            {attempt.runnerName ? `On ${attempt.runnerName}` : 'Waiting for a runner'}
            {attempt.startedAt && ` · ${duration(attempt.startedAt, attempt.finishedAt, now)}`}
          </div>
          {attempt.error && (
            <p className="border-b border-line bg-danger-soft px-4 py-2 text-sm text-danger">{attempt.error}</p>
          )}
          {attempt.steps.length > 0 && (
            <ol aria-label="Steps" className="max-h-48 shrink-0 overflow-y-auto border-b border-line bg-surface py-1">
              {attempt.steps.map((step) => {
                const { Icon, className } = stepIcons[step.status];
                const index = Number(step.index);
                return (
                  <li key={index}>
                    <button
                      type="button"
                      onClick={() => setJump({ stepIndex: index, nonce: (jump?.nonce ?? 0) + 1 })}
                      className="flex w-full items-center gap-2 px-4 py-1 text-left text-sm hover:bg-sunken"
                    >
                      <Icon aria-hidden="true" size={14} className={clsx('shrink-0', className)} />
                      <span className="w-5 shrink-0 text-right text-xs text-muted">{index + 1}</span>
                      <span className="min-w-0 flex-1 truncate">{step.name}</span>
                      <span className="shrink-0 text-xs text-muted">
                        {step.status === 'Running' || step.status === 'Sent' ? step.status.toLowerCase() : step.status}
                        {step.exitCode !== null && step.exitCode !== undefined && step.status === 'Failed'
                          ? ` (exit ${step.exitCode})`
                          : ''}
                        {step.startedAt && ` · ${duration(step.startedAt, step.finishedAt, now)}`}
                      </span>
                    </button>
                  </li>
                );
              })}
            </ol>
          )}
          <LogView
            key={attempt.id}
            log={log}
            steps={attempt.steps}
            label={`${detail.name}, attempt ${attempt.number}`}
            jump={jump}
          />
        </>
      )}
    </div>
  );
}
