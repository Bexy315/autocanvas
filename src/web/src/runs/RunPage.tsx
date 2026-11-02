/**
 * One run, filling the window like the editor: the run as a graph of its jobs with their status on
 * the left, the chosen job with its attempts, steps and live log on the right. The run is read again
 * every two seconds while it runs; the log follows live over Server-Sent Events.
 */
import { ArrowLeft, Ban, Play } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { useAutomation } from '../api/automations';
import { ApiError } from '../api/problem';
import { useCancelRun, useRun, useStartRun, type RunView } from '../api/runs';
import { AutomationCanvas } from '../canvas/AutomationCanvas';
import { graphFromYaml } from '../canvas/model';
import { hasRole } from '../lib/roles';
import { duration } from '../lib/time';
import { ErrorAlert } from '../ui/Alert';
import { RunStateBadge } from '../ui/Badge';
import { Button } from '../ui/Button';
import { RelativeTime } from '../ui/RelativeTime';
import { Loading } from '../ui/Spinner';
import { JobDetail } from './JobDetail';
import { jobMark, startedText } from './runText';
import { useNow } from './useNow';

export function RunPage() {
  const { id = '' } = useParams();
  const run = useRun(id);
  if (run.isError) {
    return (
      <div className="mx-auto max-w-5xl p-6">
        <ErrorAlert error={run.error} title="The run could not be loaded" />
      </div>
    );
  }
  if (!run.data) {
    return <Loading label="Loading the run…" />;
  }
  return <RunScreen key={id} run={run.data} />;
}

/** The job shown when none is chosen: the first running, else the first that failed, else the first. */
function firstOfInterest(run: RunView): string | null {
  const jobs = run.jobs;
  return (
    jobs.find((job) => job.status === 'Running')?.key ??
    jobs.find((job) => job.status === 'Failed')?.key ??
    jobs[0]?.key ??
    null
  );
}

function RunScreen({ run }: { run: RunView }) {
  const [params, setParams] = useSearchParams();
  const running = run.state === 'Running';
  const now = useNow(running);

  const chosen = params.get('job') ?? firstOfInterest(run);
  const attempt = params.get('attempt');
  const choose = (key: string) => setParams({ job: key }, { replace: true });

  const graph = useMemo(() => graphFromYaml(run.yaml).graph ?? null, [run.yaml]);
  const marks = useMemo(() => Object.fromEntries(run.jobs.map((job) => [job.key, jobMark(job, now)])), [run.jobs, now]);

  // The run's end is announced once, as it happens.
  const [announced, setAnnounced] = useState(run.state);
  const [announcement, setAnnouncement] = useState('');
  if (run.state !== announced) {
    setAnnounced(run.state);
    setAnnouncement(`Run ${run.number} ${run.state.toLowerCase()}`);
  }

  return (
    <div className="flex h-dvh flex-col bg-canvas text-ink">
      <header className="flex min-h-12 shrink-0 flex-wrap items-center gap-x-3 gap-y-2 border-b border-line bg-surface px-3 py-2">
        {run.automationId ? (
          <Link
            to={`/automations/${run.automationId}/runs`}
            aria-label={`Back to the runs of ${run.automationName}`}
            className="flex min-w-0 items-center gap-2 rounded-control px-1.5 py-1 hover:bg-sunken"
          >
            <ArrowLeft aria-hidden="true" size={16} className="shrink-0" />
            <span className="truncate font-semibold">{run.automationName}</span>
          </Link>
        ) : (
          <span className="font-semibold">{run.automationName}</span>
        )}
        <h1 className="text-sm font-semibold">Run #{run.number}</h1>
        <RunStateBadge state={run.state} />
        <span className="text-xs text-muted">
          {duration(run.createdAt, run.finishedAt, now)} · {startedText(run)} <RelativeTime value={run.createdAt} /> ·{' '}
          {run.automationId ? (
            <Link
              to={`/automations/${run.automationId}/versions/${run.automationVersion}`}
              className="underline-offset-2 hover:underline"
            >
              version {run.automationVersion}
            </Link>
          ) : (
            `version ${run.automationVersion}`
          )}
        </span>
        {run.cancelRequested && running && <span className="text-xs text-warn">Cancelling…</span>}
        {run.automationId && <RunActions run={run} automationId={run.automationId} />}
      </header>
      <output aria-live="polite" className="sr-only">
        {announcement}
      </output>

      <main className="flex min-h-0 flex-1 flex-col lg:flex-row">
        <section
          aria-label="Jobs of the run"
          className="min-h-64 border-b border-line lg:w-[45%] lg:border-r lg:border-b-0"
        >
          {graph ? (
            <AutomationCanvas
              graph={graph}
              stale={false}
              selectedJob={chosen}
              problemsByJob={{}}
              marks={marks}
              onSelectJob={choose}
            />
          ) : (
            <ul className="flex flex-col gap-1 p-3">
              {run.jobs.map((job) => (
                <li key={job.key}>
                  <button
                    type="button"
                    aria-current={job.key === chosen}
                    onClick={() => choose(job.key)}
                    className="flex w-full justify-between rounded-control px-2 py-1 text-left text-sm hover:bg-sunken aria-[current=true]:bg-accent-soft"
                  >
                    <span>{job.name}</span>
                    <span className="text-muted">{jobMark(job, now).text}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </section>
        <section aria-label="The chosen job" className="flex min-h-0 min-w-0 flex-1 flex-col">
          {chosen ? (
            <JobDetail
              key={chosen}
              runId={run.id}
              jobKey={chosen}
              attemptNumber={attempt === null ? null : Number(attempt)}
              onAttempt={(number) => setParams({ job: chosen, attempt: String(number) }, { replace: true })}
            />
          ) : (
            <p className="p-4 text-sm text-muted">The run has no jobs.</p>
          )}
        </section>
      </main>
    </div>
  );
}

/** Cancelling and starting again, for those who may (Operator); the server decides either way. */
function RunActions({ run, automationId }: { run: RunView; automationId: string }) {
  const navigate = useNavigate();
  const automation = useAutomation(automationId);
  const canOperate = hasRole(automation.data?.data.myRole, 'Operator');
  const cancel = useCancelRun(run.id);
  const start = useStartRun(automationId);
  const error = cancel.error ?? start.error;
  return (
    <div className="ml-auto flex flex-wrap items-center justify-end gap-2">
      {error && (
        <span role="alert" className="text-sm text-danger">
          {start.error instanceof ApiError && start.error.status === 409
            ? 'It is running already, and it runs one at a time.'
            : error.message}
        </span>
      )}
      {canOperate && run.state === 'Running' && !run.cancelRequested && (
        <Button busy={cancel.isPending} onClick={() => cancel.mutate()}>
          <Ban aria-hidden="true" size={14} />
          Cancel
        </Button>
      )}
      {canOperate && (
        <Button
          variant="primary"
          busy={start.isPending}
          onClick={() => start.mutate(undefined, { onSuccess: (next) => void navigate(`/runs/${next.id}`) })}
        >
          <Play aria-hidden="true" size={14} />
          Run again
        </Button>
      )}
    </div>
  );
}
