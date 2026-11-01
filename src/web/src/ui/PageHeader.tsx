import type { ReactNode } from 'react';

/** The top of a page: its title with badges, a line about it, and the page's actions. */
export function PageHeader({
  title,
  badges,
  children,
  actions,
}: {
  title: ReactNode;
  badges?: ReactNode;
  /** A line or two under the title: description, name, path. */
  children?: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <header className="flex flex-wrap items-start justify-between gap-4">
      <div className="flex min-w-0 flex-col gap-1">
        <div className="flex flex-wrap items-center gap-2">
          <h1 className="truncate text-xl font-semibold">{title}</h1>
          {badges}
        </div>
        {children && <div className="text-muted">{children}</div>}
      </div>
      {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
    </header>
  );
}

/** A titled box on a page. */
export function Section({ title, actions, children }: { title: string; actions?: ReactNode; children: ReactNode }) {
  const id = `section-${title.toLowerCase().replaceAll(/[^a-z0-9]+/g, '-')}`;
  return (
    <section aria-labelledby={id} className="rounded-panel border border-line bg-surface">
      <div className="flex items-center justify-between gap-2 border-b border-line px-4 py-2.5">
        <h2 id={id} className="font-semibold">
          {title}
        </h2>
        {actions}
      </div>
      {children}
    </section>
  );
}
