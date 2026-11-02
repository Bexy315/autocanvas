/**
 * Variables and secrets of the instance, a folder or an automation, the explanation of where each
 * value comes from, and an automation's webhook address.
 */
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { automationKeys } from './automations';
import { api, unwrap } from './client';
import type { components } from './schema';

export type VariableView = components['schemas']['VariableView'];
export type SecretView = components['schemas']['SecretView'];
export type ConfigurationView = components['schemas']['ConfigurationView'];
export type ConfigurationEntry = components['schemas']['ConfigurationEntry'];
export type ConfigurationSource = components['schemas']['ConfigurationSource'];
export type WebhookView = components['schemas']['WebhookView'];
export type CreatedWebhook = components['schemas']['CreatedWebhookView'];

/** Where values are defined. Automations have secrets only; their variables are in their file. */
export type Scope = { kind: 'instance' } | { kind: 'folder'; id: string } | { kind: 'automation'; id: string };

const scopeKey = (scope: Scope) => (scope.kind === 'instance' ? ['instance'] : [scope.kind, scope.id]);

export const configurationKeys = {
  variables: (scope: Scope) => ['variables', ...scopeKey(scope)] as const,
  secrets: (scope: Scope) => ['secrets', ...scopeKey(scope)] as const,
  explained: (scope: Scope) => ['configuration', ...scopeKey(scope)] as const,
  webhook: (automationId: string) => ['automations', automationId, 'webhook'] as const,
};

function listVariables(scope: Scope) {
  return scope.kind === 'folder'
    ? unwrap(api.GET('/api/v1/folders/{id}/variables', { params: { path: { id: scope.id } } }))
    : unwrap(api.GET('/api/v1/variables'));
}

function listSecrets(scope: Scope) {
  switch (scope.kind) {
    case 'folder':
      return unwrap(api.GET('/api/v1/folders/{id}/secrets', { params: { path: { id: scope.id } } }));
    case 'automation':
      return unwrap(api.GET('/api/v1/automations/{id}/secrets', { params: { path: { id: scope.id } } }));
    default:
      return unwrap(api.GET('/api/v1/secrets'));
  }
}

/** The variables of the instance or a folder. */
export function useVariables(scope: Scope & { kind: 'instance' | 'folder' }) {
  return useQuery({ queryKey: configurationKeys.variables(scope), queryFn: () => listVariables(scope) });
}

export function useSecrets(scope: Scope) {
  return useQuery({ queryKey: configurationKeys.secrets(scope), queryFn: () => listSecrets(scope) });
}

/** Every name that reaches a folder or an automation, with its definitions and the one that wins. */
export function useConfiguration(scope: Scope & { kind: 'folder' | 'automation' }) {
  return useQuery({
    queryKey: configurationKeys.explained(scope),
    queryFn: () =>
      scope.kind === 'folder'
        ? unwrap(api.GET('/api/v1/folders/{id}/configuration', { params: { path: { id: scope.id } } }))
        : unwrap(api.GET('/api/v1/automations/{id}/configuration', { params: { path: { id: scope.id } } })),
  });
}

/** After a change, the lists and every explanation (which may inherit it) are read again. */
async function refresh(client: ReturnType<typeof useQueryClient>, scope: Scope, what: 'variables' | 'secrets') {
  await client.invalidateQueries({ queryKey: configurationKeys[what](scope) });
  await client.invalidateQueries({ queryKey: ['configuration'] });
}

export function useSetVariable(scope: Scope & { kind: 'instance' | 'folder' }) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ name, value }: { name: string; value: string }) =>
      scope.kind === 'folder'
        ? unwrap(
            api.PUT('/api/v1/folders/{id}/variables/{name}', {
              params: { path: { id: scope.id, name } },
              body: { value },
            }),
          )
        : unwrap(api.PUT('/api/v1/variables/{name}', { params: { path: { name } }, body: { value } })),
    onSuccess: () => refresh(client, scope, 'variables'),
  });
}

export function useDeleteVariable(scope: Scope & { kind: 'instance' | 'folder' }) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (name: string) =>
      scope.kind === 'folder'
        ? unwrap(api.DELETE('/api/v1/folders/{id}/variables/{name}', { params: { path: { id: scope.id, name } } }))
        : unwrap(api.DELETE('/api/v1/variables/{name}', { params: { path: { name } } })),
    onSuccess: () => refresh(client, scope, 'variables'),
  });
}

export function useSetSecret(scope: Scope) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ name, value }: { name: string; value: string }) => {
      switch (scope.kind) {
        case 'folder':
          return unwrap(
            api.PUT('/api/v1/folders/{id}/secrets/{name}', {
              params: { path: { id: scope.id, name } },
              body: { value },
            }),
          );
        case 'automation':
          return unwrap(
            api.PUT('/api/v1/automations/{id}/secrets/{name}', {
              params: { path: { id: scope.id, name } },
              body: { value },
            }),
          );
        default:
          return unwrap(api.PUT('/api/v1/secrets/{name}', { params: { path: { name } }, body: { value } }));
      }
    },
    onSuccess: () => refresh(client, scope, 'secrets'),
  });
}

export function useDeleteSecret(scope: Scope) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (name: string) => {
      switch (scope.kind) {
        case 'folder':
          return unwrap(
            api.DELETE('/api/v1/folders/{id}/secrets/{name}', { params: { path: { id: scope.id, name } } }),
          );
        case 'automation':
          return unwrap(
            api.DELETE('/api/v1/automations/{id}/secrets/{name}', { params: { path: { id: scope.id, name } } }),
          );
        default:
          return unwrap(api.DELETE('/api/v1/secrets/{name}', { params: { path: { name } } }));
      }
    },
    onSuccess: () => refresh(client, scope, 'secrets'),
  });
}

/** The automation's webhook address, without its token. */
export function useWebhook(automationId: string) {
  return useQuery({
    queryKey: configurationKeys.webhook(automationId),
    queryFn: () =>
      unwrap(api.GET('/api/v1/automations/{automationId}/webhook', { params: { path: { automationId } } })),
  });
}

/** Creates the webhook address, replacing any earlier one; the token and secret come back only now. */
export function useCreateWebhook(automationId: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (requireSignature: boolean) =>
      unwrap(
        api.POST('/api/v1/automations/{automationId}/webhook', {
          params: { path: { automationId } },
          body: { requireSignature },
        }),
      ),
    onSuccess: () => client.invalidateQueries({ queryKey: configurationKeys.webhook(automationId) }),
  });
}

export function useDeleteWebhook(automationId: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () =>
      unwrap(api.DELETE('/api/v1/automations/{automationId}/webhook', { params: { path: { automationId } } })),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: configurationKeys.webhook(automationId) });
      await client.invalidateQueries({ queryKey: automationKeys.triggers(automationId) });
    },
  });
}
