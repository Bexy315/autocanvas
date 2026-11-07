import { Ban, CircleCheck, CircleDashed, CircleSlash, CircleX, Clock, LoaderCircle, TriangleAlert } from 'lucide-react';
import type { JobStep } from '../../api/runs';

/** The icon of a step's status, in the color of what it means. */
export const stepIcons: Record<JobStep['status'], { Icon: typeof CircleCheck; className: string }> = {
  Sent: { Icon: CircleDashed, className: 'text-muted' },
  Running: { Icon: LoaderCircle, className: 'animate-spin text-accent' },
  Succeeded: { Icon: CircleCheck, className: 'text-ok' },
  Failed: { Icon: CircleX, className: 'text-danger' },
  Cancelled: { Icon: Ban, className: 'text-muted' },
  TimedOut: { Icon: Clock, className: 'text-danger' },
  Errored: { Icon: TriangleAlert, className: 'text-danger' },
  Skipped: { Icon: CircleSlash, className: 'text-muted' },
};

/** A step's status in words, with the exit code of a failed command. */
export function stepStatusText(step: JobStep): string {
  const status =
    step.status === 'Sent'
      ? 'waiting'
      : step.status === 'Running'
        ? 'running'
        : step.status === 'TimedOut'
          ? 'Timed out'
          : step.status;
  const exit =
    step.status === 'Failed' && step.exitCode !== null && step.exitCode !== undefined ? ` (exit ${step.exitCode})` : '';
  return `${status}${exit}`;
}
