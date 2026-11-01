import { ChevronRight } from 'lucide-react';
import { Fragment } from 'react';
import { Link, useMatches } from 'react-router';
import { useCurrentPageInfo, type Crumb } from './pageContext';

/** What a route puts into the breadcrumbs when its page does not say more: `handle: { crumb: 'Account' }`. */
export interface CrumbHandle {
  crumb: string;
}

function crumbOf(handle: unknown): string | null {
  if (typeof handle === 'object' && handle !== null && 'crumb' in handle && typeof handle.crumb === 'string') {
    return handle.crumb;
  }
  return null;
}

/**
 * Where the page sits. Pages about a folder or an automation give their own path (`usePageInfo`);
 * other pages are named by their route. The last crumb is the page itself.
 */
export function Breadcrumbs() {
  const info = useCurrentPageInfo();
  const matches = useMatches();
  const crumbs: Crumb[] =
    info?.crumbs ??
    matches.flatMap((match) => {
      const crumb = crumbOf(match.handle);
      return crumb === null ? [] : [{ label: crumb, to: match.pathname }];
    });

  return (
    <nav aria-label="Breadcrumbs" className="min-w-0">
      <ol className="flex items-center gap-1 text-sm">
        {crumbs.map(({ label, to }, index) => {
          const last = index === crumbs.length - 1;
          return (
            <Fragment
              key={crumbs
                .slice(0, index + 1)
                .map((crumb) => crumb.label)
                .join('/')}
            >
              {index > 0 && <ChevronRight aria-hidden="true" size={14} className="shrink-0 text-muted" />}
              <li className="truncate">
                {last ? (
                  <span aria-current="page" className="font-medium text-ink">
                    {label}
                  </span>
                ) : to ? (
                  <Link to={to} className="text-muted hover:text-ink">
                    {label}
                  </Link>
                ) : (
                  <span className="text-muted">{label}</span>
                )}
              </li>
            </Fragment>
          );
        })}
      </ol>
    </nav>
  );
}
