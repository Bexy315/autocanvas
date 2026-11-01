import { clsx } from 'clsx';
import type { ReactNode } from 'react';

export type BadgeTone = 'neutral' | 'accent' | 'ok' | 'warn' | 'danger';

const tones: Record<BadgeTone, string> = {
  neutral: 'bg-sunken text-muted',
  accent: 'bg-accent-soft text-accent',
  ok: 'bg-ok-soft text-ok',
  warn: 'bg-warn-soft text-warn',
  danger: 'bg-danger-soft text-danger',
};

/** A small label for a status or a role. */
export function Badge({ tone = 'neutral', children }: { tone?: BadgeTone; children: ReactNode }) {
  return (
    <span className={clsx('inline-flex h-5 items-center rounded-full px-2 text-xs font-medium', tones[tone])}>
      {children}
    </span>
  );
}

const runTones: Record<string, BadgeTone> = {
  Running: 'accent',
  Succeeded: 'ok',
  Failed: 'danger',
  Cancelled: 'neutral',
};

/** A run's state as a badge. */
export function RunStateBadge({ state }: { state: string }) {
  return <Badge tone={runTones[state] ?? 'neutral'}>{state}</Badge>;
}
