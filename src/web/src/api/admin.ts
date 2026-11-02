/** Users, groups and runners, for admins; and the directory search everyone may use. */
import { keepPreviousData, useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from './client';
import { pageSize } from './folders';
import type { components } from './schema';

export type UserView = components['schemas']['UserView'];
export type UserSummary = components['schemas']['UserSummary'];
export type GroupView = components['schemas']['GroupView'];
export type GroupDetails = components['schemas']['GroupDetails'];
export type GroupSummary = components['schemas']['GroupSummary'];
export type RunnerView = components['schemas']['RunnerView'];
export type RegistrationToken = components['schemas']['RegistrationTokenView'];
export type TokenView = components['schemas']['TokenView'];
export type DirectoryView = components['schemas']['DirectoryView'];

/** How often the runner list is read again, so online states stay current. */
export const runnerPollMs = 10_000;

const keys = {
  users: ['admin', 'users'] as const,
  userTokens: (id: string) => ['admin', 'users', id, 'tokens'] as const,
  groups: ['admin', 'groups'] as const,
  group: (id: string) => ['admin', 'groups', id] as const,
  runners: ['admin', 'runners'] as const,
  registrationTokens: ['admin', 'registration-tokens'] as const,
  directory: (query: string) => ['directory', query] as const,
};

/** People and groups whose names contain `query` (at least two characters), for sharing. */
export function useDirectory(query: string) {
  const text = query.trim();
  return useQuery({
    queryKey: keys.directory(text),
    queryFn: ({ signal }) => unwrap(api.GET('/api/v1/directory', { params: { query: { query: text } }, signal })),
    enabled: text.length >= 2,
    placeholderData: keepPreviousData,
    staleTime: 30_000,
  });
}

export function useUsers() {
  return useInfiniteQuery({
    queryKey: keys.users,
    initialPageParam: null as string | null,
    queryFn: ({ pageParam }) =>
      unwrap(
        api.GET('/api/v1/users', {
          params: { query: { limit: pageSize, ...(pageParam ? { cursor: pageParam } : {}) } },
        }),
      ),
    getNextPageParam: (page) => page.next ?? undefined,
  });
}

export function useCreateUser() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: {
      userName: string;
      password: string;
      displayName: string | null;
      email: string | null;
      isAdmin: boolean;
    }) => unwrap(api.POST('/api/v1/users', { body })),
    onSuccess: () => client.invalidateQueries({ queryKey: keys.users }),
  });
}

export function useUpdateUser(id: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: { displayName?: string; email?: string | null; isAdmin?: boolean; isDisabled?: boolean }) =>
      unwrap(api.PATCH('/api/v1/users/{id}', { params: { path: { id } }, body })),
    onSuccess: () => client.invalidateQueries({ queryKey: keys.users }),
  });
}

export function useDeleteUser(id: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () => unwrap(api.DELETE('/api/v1/users/{id}', { params: { path: { id } } })),
    onSuccess: () => client.invalidateQueries({ queryKey: keys.users }),
  });
}

export function useResetPassword(id: string) {
  return useMutation({
    mutationFn: (password: string) =>
      unwrap(api.POST('/api/v1/users/{id}/password-reset', { params: { path: { id } }, body: { password } })),
  });
}

export function useUserTokens(id: string, enabled: boolean) {
  return useQuery({
    queryKey: keys.userTokens(id),
    queryFn: () => unwrap(api.GET('/api/v1/users/{id}/tokens', { params: { path: { id } } })),
    enabled,
  });
}

export function useRevokeUserToken(id: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (tokenId: string) =>
      unwrap(api.DELETE('/api/v1/users/{id}/tokens/{tokenId}', { params: { path: { id, tokenId } } })),
    onSuccess: () => client.invalidateQueries({ queryKey: keys.userTokens(id) }),
  });
}

export function useGroups() {
  return useInfiniteQuery({
    queryKey: keys.groups,
    initialPageParam: null as string | null,
    queryFn: ({ pageParam }) =>
      unwrap(
        api.GET('/api/v1/groups', {
          params: { query: { limit: pageSize, ...(pageParam ? { cursor: pageParam } : {}) } },
        }),
      ),
    getNextPageParam: (page) => page.next ?? undefined,
  });
}

export function useGroup(id: string | null) {
  return useQuery({
    queryKey: keys.group(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/groups/{id}', { params: { path: { id: id ?? '' } } })),
    enabled: id !== null,
  });
}

export function useCreateGroup() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: { name: string; description: string | null }) => unwrap(api.POST('/api/v1/groups', { body })),
    onSuccess: () => client.invalidateQueries({ queryKey: keys.groups }),
  });
}

export function useUpdateGroup(id: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: { name: string; description: string | null }) =>
      unwrap(api.PATCH('/api/v1/groups/{id}', { params: { path: { id } }, body })),
    onSuccess: () => client.invalidateQueries({ queryKey: keys.groups }),
  });
}

export function useDeleteGroup(id: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () => unwrap(api.DELETE('/api/v1/groups/{id}', { params: { path: { id } } })),
    onSuccess: () => client.invalidateQueries({ queryKey: keys.groups }),
  });
}

export function useGroupMember(groupId: string) {
  const client = useQueryClient();
  const refresh = async () => {
    await client.invalidateQueries({ queryKey: keys.group(groupId) });
    await client.invalidateQueries({ queryKey: keys.groups });
  };
  return {
    add: useMutation({
      mutationFn: (userId: string) =>
        unwrap(api.PUT('/api/v1/groups/{id}/members/{userId}', { params: { path: { id: groupId, userId } } })),
      onSuccess: refresh,
    }),
    remove: useMutation({
      mutationFn: (userId: string) =>
        unwrap(api.DELETE('/api/v1/groups/{id}/members/{userId}', { params: { path: { id: groupId, userId } } })),
      onSuccess: refresh,
    }),
  };
}

/** The runners, read again every ten seconds, so whether each is online stays current. */
export function useRunners() {
  return useInfiniteQuery({
    queryKey: keys.runners,
    initialPageParam: null as string | null,
    queryFn: ({ pageParam }) =>
      unwrap(
        api.GET('/api/v1/runners', {
          params: { query: { limit: pageSize, ...(pageParam ? { cursor: pageParam } : {}) } },
        }),
      ),
    getNextPageParam: (page) => page.next ?? undefined,
    refetchInterval: runnerPollMs,
  });
}

export function useUpdateRunner(id: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: { name?: string; isPaused?: boolean }) =>
      unwrap(api.PATCH('/api/v1/runners/{id}', { params: { path: { id } }, body })),
    onSuccess: () => client.invalidateQueries({ queryKey: keys.runners }),
  });
}

export function useDeleteRunner(id: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () => unwrap(api.DELETE('/api/v1/runners/{id}', { params: { path: { id } } })),
    onSuccess: () => client.invalidateQueries({ queryKey: keys.runners }),
  });
}

export function useRegistrationTokens() {
  return useQuery({
    queryKey: keys.registrationTokens,
    queryFn: () => unwrap(api.GET('/api/v1/runner-registration-tokens')),
  });
}

export function useCreateRegistrationToken() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: { description: string | null; expiresAt: string | null }) =>
      unwrap(api.POST('/api/v1/runner-registration-tokens', { body })),
    onSuccess: () => client.invalidateQueries({ queryKey: keys.registrationTokens }),
  });
}

export function useDeleteRegistrationToken() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.DELETE('/api/v1/runner-registration-tokens/{id}', { params: { path: { id } } })),
    onSuccess: () => client.invalidateQueries({ queryKey: keys.registrationTokens }),
  });
}
