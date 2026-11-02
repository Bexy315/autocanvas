import { useAutomation } from '../../api/automations';
import { useFolder } from '../../api/folders';
import type { AutomationContext } from './context';

/** An automation with its folder, as its pages need them; `context` is null until both are read. */
export function useAutomationPage(id: string) {
  const automation = useAutomation(id);
  const folder = useFolder(automation.data?.data.folderId ?? '');
  const context: AutomationContext | null =
    automation.data && folder.data
      ? { automation: automation.data.data, tag: automation.data.tag, folder: folder.data.data }
      : null;
  return {
    context,
    failed: automation.isError || folder.isError,
    error: automation.error ?? folder.error,
  };
}
