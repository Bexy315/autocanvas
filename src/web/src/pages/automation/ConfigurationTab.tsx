/**
 * What a run of the automation reads: its variables (those in its file, and those from its folders
 * and the instance) and its secrets (its own, and those from above), each tagged with where it is
 * defined.
 */
import { Pencil } from 'lucide-react';
import { Link } from 'react-router';
import { AutomationVariables, PlaceSecrets } from '../../config/ConfigurationTables';
import { hasRole } from '../../lib/roles';
import { useAutomationContext } from './context';

export function ConfigurationTab() {
  const { automation } = useAutomationContext();
  const editor = hasRole(automation.myRole, 'Editor');

  return (
    <div className="flex flex-col gap-4">
      <AutomationVariables
        automationId={automation.id}
        hint="Read as ${{ vars.NAME }}. Those in the file (variables:) override those of its folders and the instance; change them in the editor."
        action={
          editor && (
            <Link
              to={`/automations/${automation.id}/edit`}
              className="flex items-center gap-1 text-sm text-accent hover:underline"
            >
              <Pencil aria-hidden="true" size={14} />
              Change in the editor
            </Link>
          )
        }
      />
      <PlaceSecrets
        place={{ kind: 'automation', id: automation.id }}
        canEdit={editor}
        hint="Read as ${{ secrets.NAME }}. Those set here are for this automation only and override those of its folders and the instance."
      />
    </div>
  );
}
