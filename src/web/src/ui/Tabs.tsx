import { clsx } from 'clsx';
import { NavLink } from 'react-router';

/** Sections of a page as links, so each has its own address and the browser's back button works. */
export function TabLinks({
  label,
  tabs,
}: {
  label: string;
  tabs: readonly { to: string; label: string; end?: boolean }[];
}) {
  return (
    <nav aria-label={label} className="flex gap-1 border-b border-line">
      {tabs.map((tab) => (
        <NavLink
          key={tab.to}
          to={tab.to}
          end={tab.end ?? false}
          className={({ isActive }) =>
            clsx(
              '-mb-px border-b-2 px-3 py-2 text-sm',
              isActive ? 'border-accent font-medium text-ink' : 'border-transparent text-muted hover:text-ink',
            )
          }
        >
          {tab.label}
        </NavLink>
      ))}
    </nav>
  );
}
