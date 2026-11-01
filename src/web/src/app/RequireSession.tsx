import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router';
import { useMe } from '../api/session';
import { ErrorAlert } from '../ui/Alert';
import { Loading } from '../ui/Spinner';
import { returnQuery } from './returnTo';

/**
 * Shows its children only with a session. Without one it sends the user to sign in, and while a
 * password set by someone else is still in use, to change it first (the API refuses everything
 * else until then).
 */
export function RequireSession({
  children,
  passwordChange = false,
}: {
  children: ReactNode;
  passwordChange?: boolean;
}) {
  const me = useMe();
  const location = useLocation();
  const here = `${location.pathname}${location.search}`;

  if (me.isPending) {
    return <Loading />;
  }
  if (me.isError) {
    return (
      <div className="mx-auto max-w-md p-8">
        <ErrorAlert error={me.error} title="Your account could not be loaded" />
      </div>
    );
  }
  if (me.data === null) {
    return <Navigate to={`/sign-in${returnQuery(here)}`} replace />;
  }
  if (me.data.user.mustChangePassword && !passwordChange) {
    return <Navigate to={`/change-password${returnQuery(here)}`} replace />;
  }
  return children;
}
