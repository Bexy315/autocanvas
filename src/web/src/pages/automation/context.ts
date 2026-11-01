import { useOutletContext } from 'react-router';
import type { AutomationView } from '../../api/automations';
import type { FolderView } from '../../api/folders';

/** What the automation's page hands its tabs. */
export interface AutomationContext {
  automation: AutomationView;
  /** Its version as `ETag`, for `If-Match`. */
  tag: string | null;
  folder: FolderView;
}

export function useAutomationContext(): AutomationContext {
  return useOutletContext<AutomationContext>();
}
