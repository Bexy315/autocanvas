import type { FolderView } from '../api/folders';
import { FolderVariables, PlaceSecrets } from '../config/ConfigurationTables';
import { hasRole } from '../lib/roles';

/**
 * A folder's variables and secrets: its own, for everything in it, and those that reach it from
 * above, each tagged with where it is defined.
 */
export function FolderConfiguration({ folder }: { folder: FolderView }) {
  const editor = hasRole(folder.myRole, 'Editor');
  return (
    <div className="flex flex-col gap-4">
      <FolderVariables
        folderId={folder.id}
        canEdit={editor}
        hint="Read as ${{ vars.NAME }} by every automation in this folder and the folders below. Those set here override those from above."
      />
      <PlaceSecrets
        place={{ kind: 'folder', id: folder.id }}
        canEdit={editor}
        hint="Read as ${{ secrets.NAME }} by every automation in this folder and the folders below. Those set here override those from above."
      />
    </div>
  );
}
