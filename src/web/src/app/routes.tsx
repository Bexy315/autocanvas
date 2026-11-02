import { Outlet, type RouteObject } from 'react-router';
import { AppShell } from '../layout/AppShell';
import { AutomationsLayout } from '../layout/AutomationsLayout';
import type { CrumbHandle } from '../layout/Breadcrumbs';
import { AccountPage } from '../pages/AccountPage';
import { AutomationLayout } from '../pages/automation/AutomationLayout';
import { EditorLayout } from '../pages/automation/EditorLayout';
import { OverviewTab } from '../pages/automation/OverviewTab';
import { ConfigurationTab } from '../pages/automation/ConfigurationTab';
import { RunsTab } from '../pages/automation/RunsTab';
import { TriggersTab } from '../pages/automation/TriggersTab';
import { AdminPage } from '../pages/admin/AdminPage';
import { VersionsTab } from '../pages/automation/VersionsTab';
import { ChangePasswordPage } from '../pages/ChangePasswordPage';
import { FolderPage } from '../pages/FolderPage';
import { NotFoundPage } from '../pages/NotFoundPage';
import { RunsPage } from '../pages/RunsPage';
import { SignInPage } from '../pages/SignInPage';
import { StartPage } from '../pages/StartPage';
import { ErrorBoundary } from '../ui/ErrorBoundary';
import { RequireSession } from './RequireSession';
import { SessionExpiry } from './SessionExpiry';

const crumb = (name: string): CrumbHandle => ({ crumb: name });

/** Every page of the UI. The server answers each of these paths with index.html (ADR-0018). */
export const routes: RouteObject[] = [
  {
    element: <SessionExpiry />,
    children: [
      { path: '/sign-in', element: <SignInPage /> },
      {
        path: '/change-password',
        element: (
          <RequireSession passwordChange>
            <ChangePasswordPage />
          </RequireSession>
        ),
      },
      {
        // The editor fills the window, outside the app's shell.
        path: '/automations/:id/edit',
        element: (
          <RequireSession>
            <ErrorBoundary>
              <EditorLayout />
            </ErrorBoundary>
          </RequireSession>
        ),
        children: [
          {
            index: true,
            // The editor (CodeMirror, the canvas) loads when it is first opened, not with the app.
            lazy: async () => ({ Component: (await import('../pages/automation/EditorTab')).EditorTab }),
          },
        ],
      },
      {
        // A run fills the window like the editor; its page (the log, the run's graph) loads on demand.
        path: '/runs/:id',
        element: (
          <RequireSession>
            <ErrorBoundary>
              <Outlet />
            </ErrorBoundary>
          </RequireSession>
        ),
        children: [{ index: true, lazy: async () => ({ Component: (await import('../runs/RunPage')).RunPage }) }],
      },
      {
        path: '/',
        element: (
          <RequireSession>
            <ErrorBoundary>
              <AppShell />
            </ErrorBoundary>
          </RequireSession>
        ),
        children: [
          {
            // Folders and automations, with the folder tree; their pages set their own breadcrumbs.
            element: <AutomationsLayout />,
            children: [
              { index: true, element: <StartPage /> },
              { path: 'folders/:id', element: <FolderPage /> },
              { path: 'folders/:id/configuration', element: <FolderPage /> },
              {
                path: 'automations/:id',
                element: <AutomationLayout />,
                children: [
                  { index: true, element: <OverviewTab /> },
                  { path: 'runs', element: <RunsTab /> },
                  { path: 'triggers', element: <TriggersTab /> },
                  { path: 'configuration', element: <ConfigurationTab /> },
                  { path: 'versions', element: <VersionsTab /> },
                  { path: 'versions/:number', element: <VersionsTab /> },
                ],
              },
            ],
          },
          { path: 'runs', element: <RunsPage /> },
          { path: 'admin', element: <AdminPage /> },
          { path: 'account', element: <AccountPage />, handle: crumb('Account') },
          { path: '*', element: <NotFoundPage />, handle: crumb('Not found') },
        ],
      },
    ],
  },
];
