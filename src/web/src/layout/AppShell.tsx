import { clsx } from 'clsx';
import { PanelLeftClose, PanelLeftOpen, Workflow } from 'lucide-react';
import { useMemo, useState } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router';
import { useServerInfo } from '../api/session';
import { readSetting, writeSetting } from '../app/storage';
import { shortVersion } from '../app/version';
import { LogoMark } from '../ui/Logo';
import { Breadcrumbs } from './Breadcrumbs';
import { PageInfoContext, type PageInfo } from './pageContext';
import { UserMenu } from './UserMenu';

export const sidebarKey = 'autocanvas.sidebar';

/** The areas of the app. More come with their S3 steps (runs, runners, admin). */
const areas = [{ to: '/', label: 'Automations', Icon: Workflow, paths: ['/', '/folders/', '/automations/'] }] as const;

function inArea(pathname: string, paths: readonly string[]): boolean {
  return paths.some((path) => (path === '/' ? pathname === '/' : pathname.startsWith(path)));
}

/** The frame of every signed-in page: a collapsible sidebar on the left, a top bar, the page. */
export function AppShell() {
  const [collapsed, setCollapsed] = useState(() => readSetting(sidebarKey) === 'collapsed');
  const info = useServerInfo();
  const location = useLocation();
  const [pageInfo, setPageInfo] = useState<PageInfo | null>(null);
  const pageContext = useMemo(() => ({ info: pageInfo, setInfo: setPageInfo }), [pageInfo]);

  const toggle = () => {
    const next = !collapsed;
    writeSetting(sidebarKey, next ? 'collapsed' : 'expanded');
    setCollapsed(next);
  };

  return (
    <PageInfoContext value={pageContext}>
      <div className="flex h-full">
        <a
          href="#main"
          className="sr-only z-50 rounded-control bg-surface px-3 py-2 focus:not-sr-only focus:absolute focus:left-2 focus:top-2"
        >
          Skip to content
        </a>
        <nav
          aria-label="Main"
          className={clsx(
            'flex shrink-0 flex-col border-r border-line bg-surface transition-[width] duration-150',
            collapsed ? 'w-13' : 'w-56',
          )}
        >
          <div className="flex h-12 items-center gap-2 border-b border-line px-3.5">
            <LogoMark />
            {!collapsed && <span className="font-semibold tracking-tight">AutoCanvas</span>}
          </div>
          <ul className="flex flex-1 flex-col gap-0.5 p-2">
            {areas.map(({ to, label, Icon, paths }) => {
              const active = inArea(location.pathname, paths);
              return (
                <li key={to}>
                  <NavLink
                    to={to}
                    aria-current={active ? 'page' : undefined}
                    title={collapsed ? label : undefined}
                    className={clsx(
                      'flex h-8 items-center gap-2.5 rounded-control px-2 text-sm',
                      active ? 'bg-accent-soft font-medium text-accent' : 'text-muted hover:bg-sunken hover:text-ink',
                    )}
                  >
                    <Icon aria-hidden="true" size={16} className="shrink-0" />
                    <span className={clsx(collapsed && 'sr-only')}>{label}</span>
                  </NavLink>
                </li>
              );
            })}
          </ul>
          <div className="flex flex-col gap-1 border-t border-line p-2">
            <button
              type="button"
              onClick={toggle}
              aria-expanded={!collapsed}
              className="flex h-8 items-center gap-2.5 rounded-control px-2 text-sm text-muted hover:bg-sunken hover:text-ink"
            >
              {collapsed ? (
                <PanelLeftOpen aria-hidden="true" size={16} className="shrink-0" />
              ) : (
                <PanelLeftClose aria-hidden="true" size={16} className="shrink-0" />
              )}
              <span className={clsx(collapsed && 'sr-only')}>{collapsed ? 'Expand sidebar' : 'Collapse sidebar'}</span>
            </button>
            {!collapsed && info.data && (
              <p className="truncate px-2 text-xs text-muted" title={info.data.version}>
                Version {shortVersion(info.data.version)}
              </p>
            )}
          </div>
        </nav>
        <div className="flex min-w-0 flex-1 flex-col">
          <header className="flex h-12 shrink-0 items-center justify-between gap-4 border-b border-line bg-surface px-4">
            <Breadcrumbs />
            <UserMenu />
          </header>
          <main id="main" tabIndex={-1} className="min-h-0 flex-1 overflow-auto">
            <Outlet />
          </main>
        </div>
      </div>
    </PageInfoContext>
  );
}
