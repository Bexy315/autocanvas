import { clsx } from 'clsx';
import type { ReactNode } from 'react';

/** A named area that scrolls on its own, such as a file or a diff; reachable by keyboard to be read. */
export function ScrollRegion({
  label,
  className,
  children,
}: {
  label: string;
  className?: string;
  children: ReactNode;
}) {
  return (
    <section
      aria-label={label}
      // A scrolling region must be reachable by keyboard to be read (WCAG 2.1.1), hence the tab stop.
      // oxlint-disable-next-line jsx-a11y/no-noninteractive-tabindex
      tabIndex={0}
      className={clsx('overflow-auto font-mono text-xs', className)}
    >
      {children}
    </section>
  );
}
