import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from './client';
import type { components } from './schema';

export type GitHubForge = components['schemas']['GitHubForgeView'];
export type GitHubApp = NonNullable<GitHubForge['app']>;
export type GitHubInstallation = components['schemas']['GitHubInstallation'];
export type GitHubManifest = components['schemas']['GitHubManifestView'];
export type GitHubAppSettings = components['schemas']['GitHubAppSettings'];

const githubKey = ['admin', 'forges', 'github'] as const;

/** The instance's GitHub App with its installations, or `app: null` (admins only). */
export function useGitHubApp() {
  return useQuery({
    queryKey: githubKey,
    queryFn: () => unwrap(api.GET('/api/v1/forges/github')),
  });
}

/** The manifest of a new app and the GitHub address the browser posts it to. */
export function useGitHubManifest() {
  return useMutation({
    mutationFn: (body: { organization: string | null; webAddress: string | null }) =>
      unwrap(api.POST('/api/v1/forges/github/manifest', { body })),
  });
}

/** Turns the code GitHub handed back into the app. */
export function useCompleteGitHubApp() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: { code: string; state: string }) => unwrap(api.POST('/api/v1/forges/github/created', { body })),
    onSuccess: (forge) => client.setQueryData(githubKey, forge),
  });
}

/** Enters an existing app, or a new key or secret of it. */
export function useConfigureGitHubApp() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: GitHubAppSettings) => unwrap(api.PUT('/api/v1/forges/github', { body })),
    onSuccess: (forge) => client.setQueryData(githubKey, forge),
  });
}

/** Removes the app from the instance. */
export function useDeleteGitHubApp() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () => unwrap(api.DELETE('/api/v1/forges/github')),
    onSuccess: () => client.setQueryData(githubKey, { app: null }),
  });
}
