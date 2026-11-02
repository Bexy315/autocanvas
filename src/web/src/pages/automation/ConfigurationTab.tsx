/**
 * The automation's secrets, the variables in its file, and where every value a run reads comes
 * from: the instance, its folders, the automation itself.
 */
import { Pencil } from 'lucide-react';
import { Link } from 'react-router';
import { useConfiguration } from '../../api/configuration';
import { ConfigurationExplain } from '../../config/ConfigurationExplain';
import { SecretsTable } from '../../config/ScopeTables';
import { hasRole } from '../../lib/roles';
import { useAutomationContext } from './context';

export function ConfigurationTab() {
  const { automation } = useAutomationContext();
  const scope = { kind: 'automation', id: automation.id } as const;
  const editor = hasRole(automation.myRole, 'Editor');
  // The variables of the file are the definitions the automation itself makes.
  const configuration = useConfiguration(scope);
  const variables = (configuration.data?.variables ?? []).flatMap((entry) =>
    entry.definitions
      .filter((definition) => definition.kind === 'Automation')
      .map((definition) => [entry.name, definition.value ?? null] as const),
  );

  return (
    <div className="flex flex-col gap-4">
      <SecretsTable
        scope={scope}
        canEdit={editor}
        hint="Secrets of this automation only, read as ${{ secrets.NAME }}; they override those of its folders and the instance."
      />
      <section aria-label="Variables in the file" className="rounded-panel border border-line bg-surface">
        <div className="flex items-center justify-between gap-3 border-b border-line px-4 py-2.5">
          <h3 className="font-semibold">Variables in the file</h3>
          {editor && (
            <Link
              to={`/automations/${automation.id}/edit`}
              className="flex items-center gap-1 text-sm text-accent hover:underline"
            >
              <Pencil aria-hidden="true" size={14} />
              Change in the editor
            </Link>
          )}
        </div>
        {variables.length === 0 ? (
          <p className="px-4 py-3 text-sm text-muted">
            None. <code className="font-mono">variables:</code> in the file sets them for this automation.
          </p>
        ) : (
          <ul className="divide-y divide-line">
            {variables.map(([name, value]) => (
              <li key={name} className="flex gap-4 px-4 py-2">
                <span className="w-48 truncate font-mono text-sm">{name}</span>
                <span className="min-w-0 flex-1 truncate font-mono text-sm">
                  {value ?? <span className="font-sans text-muted">Hidden: values need the Operator role</span>}
                </span>
              </li>
            ))}
          </ul>
        )}
      </section>
      <ConfigurationExplain
        scope={scope}
        intro="Every name a run of this automation reads, and which definition it gets: the nearest one wins."
      />
    </div>
  );
}
