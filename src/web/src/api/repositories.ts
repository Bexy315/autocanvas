import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { api, unwrap } from './client';
import { folderKeys } from './folders';
import type { components } from './schema';

export type RepositoryView = components['schemas']['RepositoryView'];
export type GitHubRepositoryView = components['schemas']['GitHubRepositoryView'];
export type CreatedRepositoryWebhook = components['schemas']['CreatedRepositoryWebhookView'];
export type ProposedChange = components['schemas']['ProposedChangeView'];
export type NewRepository = components['schemas']['NewRepository'];
export type RepositoryChange = components['schemas']['RepositoryChange'];
export type NewGitHubRepository = components['schemas']['NewGitHubRepository'];

export const repositoryKeys = {
  inFolder: (folderId: string) => ['folders', folderId, 'repositories'] as const,
  github: (folderId: string) => ['folders', folderId, 'github-repositories'] as const,
};

/** A repository changed: its folder's list of repositories and automations are read again. */
function refresh(client: QueryClient, folderId: string) {
  void client.invalidateQueries({ queryKey: repositoryKeys.inFolder(folderId) });
  void client.invalidateQueries({ queryKey: folderKeys.automations(folderId) });
}

/** The repositories connected in a folder (Viewer). */
export function useRepositories(folderId: string) {
  return useQuery({
    queryKey: repositoryKeys.inFolder(folderId),
    queryFn: () => unwrap(api.GET('/api/v1/folders/{id}/repositories', { params: { path: { id: folderId } } })),
  });
}

/** The repositories the GitHub App reaches, to connect one in the folder (Editor). */
export function useGitHubRepositories(folderId: string, enabled: boolean) {
  return useQuery({
    queryKey: repositoryKeys.github(folderId),
    enabled,
    retry: false,
    queryFn: () => unwrap(api.GET('/api/v1/folders/{id}/github/repositories', { params: { path: { id: folderId } } })),
  });
}

export function useConnectRepository(folderId: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: NewRepository) =>
      unwrap(api.POST('/api/v1/folders/{id}/repositories', { params: { path: { id: folderId } }, body })),
    onSuccess: () => refresh(client, folderId),
  });
}

export function useConnectGitHubRepository(folderId: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: NewGitHubRepository) =>
      unwrap(api.POST('/api/v1/folders/{id}/repositories/github', { params: { path: { id: folderId } }, body })),
    onSuccess: () => refresh(client, folderId),
  });
}

export function useSyncRepository(repository: RepositoryView) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () => unwrap(api.POST('/api/v1/repositories/{id}/sync', { params: { path: { id: repository.id } } })),
    onSuccess: () => refresh(client, repository.folderId),
  });
}

export function useUpdateRepository(repository: RepositoryView) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: RepositoryChange) =>
      unwrap(api.PATCH('/api/v1/repositories/{id}', { params: { path: { id: repository.id } }, body })),
    onSuccess: () => refresh(client, repository.folderId),
  });
}

export function useDisconnectRepository(repository: RepositoryView) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () => unwrap(api.DELETE('/api/v1/repositories/{id}', { params: { path: { id: repository.id } } })),
    onSuccess: () => refresh(client, repository.folderId),
  });
}

export function useCreateRepositoryWebhook(repository: RepositoryView) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () =>
      unwrap(api.POST('/api/v1/repositories/{id}/webhook', { params: { path: { id: repository.id } } })),
    onSuccess: () => refresh(client, repository.folderId),
  });
}

export function useDeleteRepositoryWebhook(repository: RepositoryView) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () =>
      unwrap(api.DELETE('/api/v1/repositories/{id}/webhook', { params: { path: { id: repository.id } } })),
    onSuccess: () => refresh(client, repository.folderId),
  });
}

/** Proposes a change of a repository automation as a pull request (GitHub). */
export function useProposeChange(automationId: string) {
  return useMutation({
    mutationFn: (body: { yaml: string; title: string | null; message: string | null }) =>
      unwrap(api.POST('/api/v1/automations/{id}/pull-request', { params: { path: { id: automationId } }, body })),
  });
}
