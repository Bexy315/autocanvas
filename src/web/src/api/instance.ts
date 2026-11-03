import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from './client';
import type { components } from './schema';

export type InstanceSettings = components['schemas']['InstanceSettingsView'];

const instanceKey = ['admin', 'instance'] as const;

/** The installation's addresses and where each comes from (admins only). */
export function useInstanceSettings(enabled = true) {
  return useQuery({
    queryKey: instanceKey,
    queryFn: () => unwrap(api.GET('/api/v1/instance')),
    enabled,
  });
}

export function useUpdateInstanceSettings() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: { publicAddress: string | null; runnerAddress: string | null }) =>
      unwrap(api.PUT('/api/v1/instance', { body })),
    onSuccess: (settings) => {
      client.setQueryData(instanceKey, settings);
    },
  });
}
