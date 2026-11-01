import { FolderPlus, Folders } from 'lucide-react';
import { useState } from 'react';
import { useStartFolders } from '../api/folders';
import { CreateFolderDialog } from '../dialogs/FolderDialogs';
import { usePageInfo } from '../layout/pageContext';
import { ErrorAlert } from '../ui/Alert';
import { Button } from '../ui/Button';
import { EmptyState } from '../ui/EmptyState';
import { PageHeader } from '../ui/PageHeader';
import { Loading } from '../ui/Spinner';
import { FolderRow } from './folderRows';

/** Where browsing starts: the topmost folders the user may see. */
export function StartPage() {
  const folders = useStartFolders();
  const [creating, setCreating] = useState(false);
  usePageInfo({ crumbs: [{ label: 'Automations', to: null }] });

  const newFolder = (
    <Button variant="primary" onClick={() => setCreating(true)}>
      <FolderPlus aria-hidden="true" size={16} />
      New folder
    </Button>
  );

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-6 p-6">
      <PageHeader title="Automations" actions={folders.data?.length ? newFolder : undefined}>
        Automations live in folders; a folder decides who may see and change what is in it.
      </PageHeader>
      {folders.isPending && <Loading label="Loading folders…" />}
      {folders.isError && <ErrorAlert error={folders.error} title="The folders could not be loaded" />}
      {folders.data?.length === 0 && (
        <EmptyState Icon={Folders} title="No folders yet" action={newFolder}>
          Create a folder for your first automations. You become its owner and can share it later.
        </EmptyState>
      )}
      {folders.data && folders.data.length > 0 && (
        <ul aria-label="Folders" className="divide-y divide-line rounded-panel border border-line bg-surface">
          {folders.data.map((folder) => (
            <FolderRow
              key={folder.id}
              folder={folder}
              {...(folder.path.length > 0 ? { path: folder.path.map((crumb) => crumb.name).join(' / ') } : {})}
            />
          ))}
        </ul>
      )}
      <CreateFolderDialog parent={null} open={creating} onOpenChange={setCreating} />
    </div>
  );
}
