import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from './client';
import type { components } from './schema';
import { meKey } from './session';

export type SetupStatus = components['schemas']['SetupStatus'];
export type SetupDetails = components['schemas']['SetupDetails'];
export type SetupRequest = components['schemas']['SetupRequest'];
export type SetupCompleted = components['schemas']['SetupCompleted'];
export type SettingValue = components['schemas']['SettingValue'];

/** The header the setup token travels in (ADR-0020). */
export const setupTokenHeader = 'X-AutoCanvas-Setup-Token';

/**
 * Whether the installation still needs to be set up, and with the token what the setup asks about.
 * A wrong token is an error (401), so the token step can say so.
 */
export function useSetupStatus(token: string | null) {
  return useQuery({
    queryKey: ['setup', token],
    queryFn: () => unwrap(api.GET('/api/v1/setup', { headers: token ? { [setupTokenHeader]: token } : {} })),
    retry: false,
    staleTime: Infinity,
  });
}

/** Sets the installation up; the new admin is signed in by the answer. */
export function useCompleteSetup() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ token, body }: { token: string; body: SetupRequest }) =>
      unwrap(api.POST('/api/v1/setup', { body, headers: { [setupTokenHeader]: token } })),
    onSuccess: (completed) => {
      client.setQueryData(meKey, completed.me);
    },
  });
}

/**
 * The first runner that is online, read every three seconds while the setup waits for the bundled
 * runner to register; `null` until one is.
 */
export function useOnlineRunner(enabled: boolean) {
  return useQuery({
    queryKey: ['setup', 'runner'],
    queryFn: async () => {
      const page = await unwrap(api.GET('/api/v1/runners', { params: { query: { limit: 20 } } }));
      return page.items.find((runner) => runner.isOnline) ?? null;
    },
    enabled,
    refetchInterval: (query) => (query.state.data ? false : 3_000),
  });
}
