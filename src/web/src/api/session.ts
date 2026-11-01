import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from './client';
import { ApiError } from './problem';
import type { components } from './schema';

export type Me = components['schemas']['MeView'];

export const meKey = ['me'] as const;

/** The signed-in user, or `null` when there is no session. */
export function useMe() {
  return useQuery({
    queryKey: meKey,
    queryFn: async (): Promise<Me | null> => {
      try {
        return await unwrap(api.GET('/api/v1/me'));
      } catch (error) {
        if (error instanceof ApiError && error.status === 401) {
          return null;
        }
        throw error;
      }
    },
    staleTime: 60_000,
    retry: false,
  });
}

/** Product and version of the server. */
export function useServerInfo() {
  return useQuery({
    queryKey: ['info'],
    queryFn: () => unwrap(api.GET('/api/v1/info')),
    staleTime: Infinity,
  });
}

export function useSignIn() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: { userName: string; password: string }) => unwrap(api.POST('/api/v1/auth/sign-in', { body })),
    onSuccess: (signedIn) => {
      client.setQueryData(meKey, signedIn.me);
    },
  });
}

export function useSignOut() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () => unwrap(api.POST('/api/v1/auth/sign-out')),
    onSettled: () => {
      // Whatever the server said, nothing of this user stays in the page.
      client.clear();
      client.setQueryData(meKey, null);
    },
  });
}

export function useChangePassword() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: { currentPassword: string; newPassword: string }) =>
      unwrap(api.PUT('/api/v1/me/password', { body })),
    onSuccess: () => {
      // Known at once, so the page can move on before the account is read again.
      client.setQueryData<Me | null>(meKey, (me) =>
        me ? { ...me, user: { ...me.user, mustChangePassword: false } } : me,
      );
      return client.invalidateQueries({ queryKey: meKey });
    },
  });
}
