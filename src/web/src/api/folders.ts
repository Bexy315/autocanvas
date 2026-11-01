import { useInfiniteQuery, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { api, ifMatch, unwrap, unwrapWithTag } from './client';
import type { components } from './schema';

export type FolderView = components['schemas']['FolderView'];
export type FolderSummary = components['schemas']['FolderSummary'];
export type AutomationSummary = components['schemas']['AutomationSummary'];

/** How many entries a list asks for per page; "Load more" asks for the next. */
export const pageSize = 50;

export const folderKeys = {
  all: ['folders'] as const,
  start: ['folders', 'start'] as const,
  folder: (id: string) => ['folders', id] as const,
  children: (id: string) => ['folders', id, 'children'] as const,
  automations: (id: string) => ['folders', id, 'automations'] as const,
};

/** The topmost folders the user may see: where browsing starts. */
export function useStartFolders() {
  return useQuery({
    queryKey: folderKeys.start,
    queryFn: () => unwrap(api.GET('/api/v1/folders')),
  });
}

/** A folder with its path, the user's role and its version (`tag` for `If-Match`). */
export function useFolder(id: string) {
  return useQuery({
    queryKey: folderKeys.folder(id),
    queryFn: () => unwrapWithTag(api.GET('/api/v1/folders/{id}', { params: { path: { id } } })),
  });
}

export function useChildren(id: string, enabled = true) {
  return useInfiniteQuery({
    queryKey: folderKeys.children(id),
    enabled,
    initialPageParam: null as string | null,
    queryFn: ({ pageParam }) =>
      unwrap(
        api.GET('/api/v1/folders/{id}/children', {
          params: { path: { id }, query: { limit: pageSize, ...(pageParam ? { cursor: pageParam } : {}) } },
        }),
      ),
    getNextPageParam: (page) => page.next ?? undefined,
  });
}

export function useFolderAutomations(id: string) {
  return useInfiniteQuery({
    queryKey: folderKeys.automations(id),
    initialPageParam: null as string | null,
    queryFn: ({ pageParam }) =>
      unwrap(
        api.GET('/api/v1/folders/{folderId}/automations', {
          params: { path: { folderId: id }, query: { limit: pageSize, ...(pageParam ? { cursor: pageParam } : {}) } },
        }),
      ),
    getNextPageParam: (page) => page.next ?? undefined,
  });
}

/** After a folder changed: everything about folders is read again (lists, tree, paths). */
export function refreshFolders(client: QueryClient) {
  return client.invalidateQueries({ queryKey: folderKeys.all });
}

export function useCreateFolder() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: { name: string; description: string | null; parentId: string | null }) =>
      unwrap(api.POST('/api/v1/folders', { body })),
    onSuccess: () => refreshFolders(client),
  });
}

export function useUpdateFolder(id: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ tag, ...body }: { name: string; description: string | null; tag: string | null }) =>
      unwrap(api.PATCH('/api/v1/folders/{id}', { params: { path: { id } }, body, headers: ifMatch(tag) })),
    onSettled: () => refreshFolders(client),
  });
}

export function useMoveFolder(id: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ parentId, tag }: { parentId: string | null; tag: string | null }) =>
      unwrap(
        api.POST('/api/v1/folders/{id}/move', { params: { path: { id } }, body: { parentId }, headers: ifMatch(tag) }),
      ),
    onSettled: () => refreshFolders(client),
  });
}

export function useDeleteFolder(id: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ tag }: { tag: string | null }) =>
      unwrap(api.DELETE('/api/v1/folders/{id}', { params: { path: { id } }, headers: ifMatch(tag) })),
    onSuccess: () => {
      client.removeQueries({ queryKey: folderKeys.folder(id) });
      return refreshFolders(client);
    },
  });
}
