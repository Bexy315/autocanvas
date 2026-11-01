import { useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { Outlet, useLocation, useNavigate } from 'react-router';
import { sessionEvents, sessionExpired } from '../api/client';
import { meKey } from '../api/session';
import { returnQuery } from './returnTo';

/**
 * The root of every route: when the server says the session is gone (signed out elsewhere,
 * expired, password changed on another device), the page forgets the user and asks to sign in
 * again, coming back here afterwards.
 */
export function SessionExpiry() {
  const client = useQueryClient();
  const navigate = useNavigate();
  const location = useLocation();
  const here = `${location.pathname}${location.search}`;

  useEffect(() => {
    const onExpired = () => {
      client.clear();
      client.setQueryData(meKey, null);
      void navigate(`/sign-in${returnQuery(here, { expired: '1' })}`, { replace: true });
    };
    sessionEvents.addEventListener(sessionExpired, onExpired);
    return () => sessionEvents.removeEventListener(sessionExpired, onExpired);
  }, [client, navigate, here]);

  return <Outlet />;
}
