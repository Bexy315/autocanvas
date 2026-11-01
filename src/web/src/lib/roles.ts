import type { components } from '../api/schema';

export type FolderRole = components['schemas']['FolderRole'];

const order: readonly FolderRole[] = ['None', 'Viewer', 'Operator', 'Editor', 'Owner'];

/**
 * Whether `role` includes `needed` (ADR-0012: each role includes the ones before it). Only decides
 * what the UI offers; the server checks every request itself.
 */
export function hasRole(role: FolderRole | null | undefined, needed: FolderRole): boolean {
  return order.indexOf(role ?? 'None') >= order.indexOf(needed);
}
