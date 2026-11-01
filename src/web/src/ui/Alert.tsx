import { clsx } from 'clsx';
import { CircleAlert, CircleCheck, Info, TriangleAlert } from 'lucide-react';
import type { ReactNode } from 'react';
import { describeError } from '../api/problem';

type Tone = 'info' | 'ok' | 'warn' | 'danger';

const tones: Record<Tone, { box: string; Icon: typeof Info }> = {
  info: { box: 'bg-accent-soft text-ink border-accent/30', Icon: Info },
  ok: { box: 'bg-ok-soft text-ink border-ok/30', Icon: CircleCheck },
  warn: { box: 'bg-warn-soft text-ink border-warn/30', Icon: TriangleAlert },
  danger: { box: 'bg-danger-soft text-ink border-danger/30', Icon: CircleAlert },
};

const iconColors: Record<Tone, string> = {
  info: 'text-accent',
  ok: 'text-ok',
  warn: 'text-warn',
  danger: 'text-danger',
};

export function Alert({ tone = 'info', title, children }: { tone?: Tone; title?: string; children?: ReactNode }) {
  const { box, Icon } = tones[tone];
  return (
    <div
      role={tone === 'danger' ? 'alert' : 'status'}
      className={clsx('flex gap-2 rounded-control border px-3 py-2 text-sm', box)}
    >
      <Icon aria-hidden="true" size={16} className={clsx('mt-0.5 shrink-0', iconColors[tone])} />
      <div className="flex flex-col gap-0.5">
        {title && <p className="font-medium">{title}</p>}
        {children && <div>{children}</div>}
      </div>
    </div>
  );
}

/** What went wrong with a request, in the words the server used. */
export function ErrorAlert({ error, title }: { error: unknown; title?: string }) {
  return (
    <Alert tone="danger" {...(title === undefined ? {} : { title })}>
      {describeError(error)}
    </Alert>
  );
}
