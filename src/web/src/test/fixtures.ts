import type { components } from '../api/schema';

type Schemas = components['schemas'];
type Role = Schemas['FolderRole'];

/** A folder as `GET /folders/{id}` returns it. */
export function folderView(
  id: string,
  name: string,
  options: {
    path?: { id: string | null; name: string }[];
    role?: Role;
    parentId?: string | null;
    restricted?: boolean;
    description?: string | null;
  } = {},
): Schemas['FolderView'] {
  return {
    id,
    name,
    description: options.description ?? null,
    parentId: options.parentId ?? options.path?.at(-1)?.id ?? null,
    isRestricted: options.restricted ?? false,
    path: options.path ?? [],
    myRole: options.role ?? 'Owner',
    createdAt: '2026-11-01T10:00:00Z',
    version: 3,
  };
}

export function folderSummary(id: string, name: string, role: Role = 'Owner'): Schemas['FolderSummary'] {
  return { id, name, description: null, isRestricted: false, myRole: role };
}

export function automationSummary(id: string, name: string, title = name): Schemas['AutomationSummary'] {
  return {
    id,
    name,
    title,
    description: null,
    currentVersion: 2,
    updatedAt: new Date(Date.now() - 2 * 3600_000).toISOString(),
  };
}

export const twoJobs =
  'version: 1\nname: Build\ntriggers:\n  manual: true\njobs:\n  build:\n    steps:\n      - run: make\n';

export function automationView(
  id: string,
  folderId: string,
  options: { role?: Role; version?: number; yaml?: string; warnings?: Schemas['DiagnosticView'][] } = {},
): Schemas['AutomationView'] {
  return {
    id,
    folderId,
    name: 'build',
    title: 'Build',
    description: 'Builds the shop.',
    currentVersion: options.version ?? 2,
    yaml: options.yaml ?? twoJobs,
    warnings: options.warnings ?? [],
    myRole: options.role ?? 'Editor',
    createdAt: '2026-11-01T10:00:00Z',
    updatedAt: '2026-11-01T12:00:00Z',
  };
}

export function page<T>(items: T[], next: string | null = null) {
  return { items, next };
}
