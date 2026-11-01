import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, ifMatch, unwrap, unwrapWithTag } from './client';
import { folderKeys, pageSize, refreshFolders } from './folders';
import type { components } from './schema';

export type AutomationView = components['schemas']['AutomationView'];
export type AutomationTriggers = components['schemas']['AutomationTriggersView'];
export type RunSummary = components['schemas']['RunSummary'];
export type VersionSummary = components['schemas']['AutomationVersionSummary'];

export const automationKeys = {
  automation: (id: string) => ['automations', id] as const,
  triggers: (id: string) => ['automations', id, 'triggers'] as const,
  runs: (id: string) => ['automations', id, 'runs'] as const,
  versions: (id: string) => ['automations', id, 'versions'] as const,
  version: (id: string, number: number) => ['automations', id, 'versions', number] as const,
};

/** An automation with its current file, the user's role and its version (`tag` for `If-Match`). */
export function useAutomation(id: string) {
  return useQuery({
    queryKey: automationKeys.automation(id),
    queryFn: () => unwrapWithTag(api.GET('/api/v1/automations/{id}', { params: { path: { id } } })),
  });
}

export function useTriggers(id: string) {
  return useQuery({
    queryKey: automationKeys.triggers(id),
    queryFn: () => unwrap(api.GET('/api/v1/automations/{id}/triggers', { params: { path: { id } } })),
  });
}

/** The latest runs, newest first. */
export function useRecentRuns(id: string, limit = 5) {
  return useQuery({
    queryKey: [...automationKeys.runs(id), limit],
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/automations/{automationId}/runs', {
          params: { path: { automationId: id }, query: { limit } },
        }),
      ),
  });
}

export function useVersions(id: string) {
  return useInfiniteQuery({
    queryKey: automationKeys.versions(id),
    initialPageParam: null as string | null,
    queryFn: ({ pageParam }) =>
      unwrap(
        api.GET('/api/v1/automations/{id}/versions', {
          params: { path: { id }, query: { limit: pageSize, ...(pageParam ? { cursor: pageParam } : {}) } },
        }),
      ),
    getNextPageParam: (page) => page.next ?? undefined,
  });
}

/** One version with its file; versions never change, so it is read once. */
export function useVersion(id: string, number: number | null) {
  return useQuery({
    queryKey: automationKeys.version(id, number ?? 0),
    enabled: number !== null && number > 0,
    staleTime: Infinity,
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/automations/{id}/versions/{number}', {
          params: { path: { id, number: number ?? 0 } },
        }),
      ),
  });
}

export function useCreateAutomation(folderId: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: { name: string; yaml: string; message: string | null }) =>
      unwrap(api.POST('/api/v1/folders/{folderId}/automations', { params: { path: { folderId } }, body })),
    onSuccess: () => client.invalidateQueries({ queryKey: folderKeys.automations(folderId) }),
  });
}

/** Saves a file as the newest version; restoring an old version saves its file again. */
export function useSaveAutomation(id: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ tag, ...body }: { yaml: string; message: string | null; tag: string | null }) =>
      unwrap(api.PUT('/api/v1/automations/{id}', { params: { path: { id } }, body, headers: ifMatch(tag) })),
    onSettled: () => client.invalidateQueries({ queryKey: automationKeys.automation(id) }),
  });
}

export function useRenameAutomation(id: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ name, tag }: { name: string; tag: string | null }) =>
      unwrap(
        api.PATCH('/api/v1/automations/{id}', { params: { path: { id } }, body: { name }, headers: ifMatch(tag) }),
      ),
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: automationKeys.automation(id) });
      await refreshFolders(client);
    },
  });
}

export function useMoveAutomation(id: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ folderId, tag }: { folderId: string; tag: string | null }) =>
      unwrap(
        api.POST('/api/v1/automations/{id}/move', {
          params: { path: { id } },
          body: { folderId },
          headers: ifMatch(tag),
        }),
      ),
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: automationKeys.automation(id) });
      await refreshFolders(client);
    },
  });
}

export function useDeleteAutomation(id: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ tag }: { tag: string | null }) =>
      unwrap(api.DELETE('/api/v1/automations/{id}', { params: { path: { id } }, headers: ifMatch(tag) })),
    onSuccess: () => {
      client.removeQueries({ queryKey: automationKeys.automation(id) });
      return refreshFolders(client);
    },
  });
}

/** The URL that downloads the current file, with the session cookie, as `<name>.yaml`. */
export function downloadUrl(id: string): string {
  return `/api/v1/automations/${encodeURIComponent(id)}/yaml`;
}
