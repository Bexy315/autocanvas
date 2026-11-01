import type { LucideIcon } from 'lucide-react';
import type { ReactNode } from 'react';

/** What an empty list says, and what to do about it. */
export function EmptyState({
  Icon,
  title,
  children,
  action,
}: {
  Icon: LucideIcon;
  title: string;
  children?: ReactNode;
  action?: ReactNode;
}) {
  return (
    <div className="canvas-grid flex flex-col items-center gap-3 rounded-panel border border-dashed border-line-strong px-6 py-12 text-center">
      <span className="flex size-10 items-center justify-center rounded-panel bg-accent-soft text-accent">
        <Icon aria-hidden="true" size={20} />
      </span>
      <h2 className="text-base font-semibold">{title}</h2>
      {children && <div className="max-w-md text-muted">{children}</div>}
      {action}
    </div>
  );
}
