import type { RouteObject } from 'react-router';
import { AppShell } from '../layout/AppShell';
import { AutomationsLayout } from '../layout/AutomationsLayout';
import type { CrumbHandle } from '../layout/Breadcrumbs';
import { AccountPage } from '../pages/AccountPage';
import { AutomationLayout } from '../pages/automation/AutomationLayout';
import { OverviewTab } from '../pages/automation/OverviewTab';
import { VersionsTab } from '../pages/automation/VersionsTab';
import { ChangePasswordPage } from '../pages/ChangePasswordPage';
import { FolderPage } from '../pages/FolderPage';
import { NotFoundPage } from '../pages/NotFoundPage';
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
              {
                path: 'automations/:id',
                element: <AutomationLayout />,
                children: [
                  { index: true, element: <OverviewTab /> },
                  {
                    path: 'edit',
                    // The editor (CodeMirror) loads when it is first opened, not with the app.
                    lazy: async () => ({ Component: (await import('../pages/automation/EditorTab')).EditorTab }),
                  },
                  { path: 'versions', element: <VersionsTab /> },
                  { path: 'versions/:number', element: <VersionsTab /> },
                ],
              },
            ],
          },
          { path: 'account', element: <AccountPage />, handle: crumb('Account') },
          { path: '*', element: <NotFoundPage />, handle: crumb('Not found') },
        ],
      },
    ],
  },
];
