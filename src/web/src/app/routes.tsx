import type { RouteObject } from 'react-router';
import { AppShell } from '../layout/AppShell';
import type { CrumbHandle } from '../layout/Breadcrumbs';
import { AccountPage } from '../pages/AccountPage';
import { AutomationsPage } from '../pages/AutomationsPage';
import { ChangePasswordPage } from '../pages/ChangePasswordPage';
import { NotFoundPage } from '../pages/NotFoundPage';
import { SignInPage } from '../pages/SignInPage';
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
          { index: true, element: <AutomationsPage />, handle: crumb('Automations') },
          { path: 'account', element: <AccountPage />, handle: crumb('Account') },
          { path: '*', element: <NotFoundPage />, handle: crumb('Not found') },
        ],
      },
    ],
  },
];
