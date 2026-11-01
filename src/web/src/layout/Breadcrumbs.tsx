import { ChevronRight } from 'lucide-react';
import { Fragment } from 'react';
import { Link, useMatches } from 'react-router';

/** What a route puts into the breadcrumbs: `handle: { crumb: 'Automations' }`. */
export interface CrumbHandle {
  crumb: string;
}

function crumbOf(handle: unknown): string | null {
  if (typeof handle === 'object' && handle !== null && 'crumb' in handle && typeof handle.crumb === 'string') {
    return handle.crumb;
  }
  return null;
}

/** Where the page sits, from the routes that name themselves; the last one is the page itself. */
export function Breadcrumbs() {
  const crumbs = useMatches().flatMap((match) => {
    const crumb = crumbOf(match.handle);
    return crumb === null ? [] : [{ crumb, path: match.pathname }];
  });

  return (
    <nav aria-label="Breadcrumbs" className="min-w-0">
      <ol className="flex items-center gap-1 text-sm">
        {crumbs.map(({ crumb, path }, index) => {
          const last = index === crumbs.length - 1;
          return (
            <Fragment key={path}>
              {index > 0 && <ChevronRight aria-hidden="true" size={14} className="shrink-0 text-muted" />}
              <li className="truncate">
                {last ? (
                  <span aria-current="page" className="font-medium text-ink">
                    {crumb}
                  </span>
                ) : (
                  <Link to={path} className="text-muted hover:text-ink">
                    {crumb}
                  </Link>
                )}
              </li>
            </Fragment>
          );
        })}
      </ol>
    </nav>
  );
}
