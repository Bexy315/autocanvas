import type { FolderView } from '../api/folders';
import { ConfigurationExplain } from '../config/ConfigurationExplain';
import { SecretsTable, VariablesTable } from '../config/ScopeTables';
import { hasRole } from '../lib/roles';

/** A folder's variables and secrets, for everything in it, and what reaches it from above. */
export function FolderConfiguration({ folder }: { folder: FolderView }) {
  const scope = { kind: 'folder', id: folder.id } as const;
  const editor = hasRole(folder.myRole, 'Editor');
  return (
    <div className="flex flex-col gap-4">
      <VariablesTable
        scope={scope}
        canEdit={editor}
        hint="Read as ${{ vars.NAME }} by every automation in this folder and the folders below; they override those above."
      />
      <SecretsTable
        scope={scope}
        canEdit={editor}
        hint="Read as ${{ secrets.NAME }} by every automation in this folder and the folders below."
      />
      <ConfigurationExplain
        scope={scope}
        intro="Every name that reaches this folder, from the instance down, and which definition wins here."
      />
    </div>
  );
}
