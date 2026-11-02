import type { RunJobView, RunSummary } from '../api/runs';
import type { RunMark } from '../canvas/flow';
import { duration } from '../lib/time';

const triggers: Record<RunSummary['trigger'], string> = {
  Manual: 'by hand',
  Schedule: 'on a schedule',
  Webhook: 'by a webhook',
  AutomationCompleted: 'after another automation',
};

/** What started a run, in words: `Started by hand by ada`. */
export function startedText(run: Pick<RunSummary, 'trigger' | 'triggeredByName'>): string {
  const how = triggers[run.trigger];
  return run.trigger === 'Manual'
    ? `Started ${how} by ${run.triggeredByName}`
    : `Started ${how} (${run.triggeredByName})`;
}

/** A job's status as its card shows it in a run. */
export function jobMark(job: RunJobView, now: Date): RunMark {
  const took = job.startedAt ? duration(job.startedAt, job.finishedAt, now) : null;
  const text = (() => {
    switch (job.status) {
      case 'Pending':
        return 'Waiting';
      case 'Running':
        return took ? `Running for ${took}` : 'Starting';
      case 'Skipped':
        return job.reason ? `Skipped: ${job.reason}` : 'Skipped';
      default: {
        const attempts = Number(job.attempts) > 1 ? ` after ${job.attempts} attempts` : '';
        const taken = job.activatedCase ? `, took ${job.activatedCase}` : '';
        return `${job.status}${took ? ` in ${took}` : ''}${attempts}${taken}`;
      }
    }
  })();
  return { status: job.status, text };
}
