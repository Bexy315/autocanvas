/** Who may do what in a folder: its grants and whether it is restricted; and one's own account. */
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, ifMatch, unwrap } from './client';
import { folderKeys } from './folders';
import type { components } from './schema';

export type GrantView = components['schemas']['GrantView'];
export type FolderRole = components['schemas']['FolderRole'];
export type SessionView = components['schemas']['SessionView'];
export type MyTokenView = components['schemas']['TokenView'];

const grantsKey = (folderId: string) => ['folders', folderId, 'grants'] as const;

export function useGrants(folderId: string, enabled: boolean) {
  return useQuery({
    queryKey: grantsKey(folderId),
    queryFn: () => unwrap(api.GET('/api/v1/folders/{id}/grants', { params: { path: { id: folderId } } })),
    enabled,
  });
}

/** Gives a user or a group a role on the folder, replacing the one they had there. */
export function useSetGrant(folderId: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: { role: FolderRole; userId?: string; groupId?: string }) =>
      unwrap(api.PUT('/api/v1/folders/{id}/grants', { params: { path: { id: folderId } }, body })),
    onSuccess: () => client.invalidateQueries({ queryKey: grantsKey(folderId) }),
  });
}

export function useRemoveGrant(folderId: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (grantId: string) =>
      unwrap(api.DELETE('/api/v1/folders/{id}/grants/{grantId}', { params: { path: { id: folderId, grantId } } })),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: grantsKey(folderId) });
      await client.invalidateQueries({ queryKey: folderKeys.folder(folderId) });
    },
  });
}

export function useSetRestriction(folderId: string, tag: string | null) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (isRestricted: boolean) =>
      unwrap(
        api.PUT('/api/v1/folders/{id}/restriction', {
          params: { path: { id: folderId } },
          headers: ifMatch(tag),
          body: { isRestricted },
        }),
      ),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: folderKeys.folder(folderId) });
      await client.invalidateQueries({ queryKey: grantsKey(folderId) });
    },
  });
}

const sessionsKey = ['me', 'sessions'] as const;
const tokensKey = ['me', 'tokens'] as const;

export function useMySessions() {
  return useQuery({ queryKey: sessionsKey, queryFn: () => unwrap(api.GET('/api/v1/me/sessions')) });
}

export function useEndSession() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => unwrap(api.DELETE('/api/v1/me/sessions/{id}', { params: { path: { id } } })),
    onSuccess: () => client.invalidateQueries({ queryKey: sessionsKey }),
  });
}

export function useMyTokens() {
  return useQuery({ queryKey: tokensKey, queryFn: () => unwrap(api.GET('/api/v1/me/tokens')) });
}

export function useCreateMyToken() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: { name: string; expiresAt: string | null }) => unwrap(api.POST('/api/v1/me/tokens', { body })),
    onSuccess: () => client.invalidateQueries({ queryKey: tokensKey }),
  });
}

export function useDeleteMyToken() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => unwrap(api.DELETE('/api/v1/me/tokens/{id}', { params: { path: { id } } })),
    onSuccess: () => client.invalidateQueries({ queryKey: tokensKey }),
  });
}
