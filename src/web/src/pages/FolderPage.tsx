import { FolderInput, FolderOpen, FolderPlus, Lock, Pencil, Plus, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { useMatch, useNavigate, useParams } from 'react-router';
import {
  useChildren,
  useDeleteFolder,
  useFolder,
  useFolderAutomations,
  useMoveFolder,
  type FolderView,
} from '../api/folders';
import { CreateAutomationDialog } from '../dialogs/AutomationDialogs';
import { ConfirmDialog } from '../dialogs/ConfirmDialog';
import { CreateFolderDialog, EditFolderDialog } from '../dialogs/FolderDialogs';
import { MoveDialog } from '../dialogs/MoveDialog';
import { usePageInfo } from '../layout/pageContext';
import { hasRole } from '../lib/roles';
import { ErrorAlert } from '../ui/Alert';
import { ActionsMenu, type Action } from '../ui/ActionsMenu';
import { Badge } from '../ui/Badge';
import { Button } from '../ui/Button';
import { EmptyState } from '../ui/EmptyState';
import { PageHeader } from '../ui/PageHeader';
import { Loading } from '../ui/Spinner';
import { TabLinks } from '../ui/Tabs';
import { FolderConfiguration } from './FolderConfiguration';
import { AutomationRow, FolderRow } from './folderRows';
import { folderPageInfo } from './folderPageInfo';

type Open = 'folder' | 'automation' | 'rename' | 'move' | 'delete' | null;

export function FolderPage() {
  const { id = '' } = useParams();
  const folder = useFolder(id);
  usePageInfo(folder.data ? folderPageInfo(folder.data.data) : null);

  if (folder.isPending) {
    return <Loading label="Loading the folder…" />;
  }
  if (folder.isError) {
    return (
      <div className="mx-auto max-w-5xl p-6">
        <ErrorAlert error={folder.error} title="The folder could not be loaded" />
      </div>
    );
  }
  return <FolderContent key={id} folder={folder.data.data} tag={folder.data.tag} />;
}

function FolderContent({ folder, tag }: { folder: FolderView; tag: string | null }) {
  const [open, setOpen] = useState<Open>(null);
  const navigate = useNavigate();
  const children = useChildren(folder.id);
  const automations = useFolderAutomations(folder.id);
  const move = useMoveFolder(folder.id);
  const remove = useDeleteFolder(folder.id);
  const configuring = useMatch('/folders/:id/configuration') !== null;
  const editor = hasRole(folder.myRole, 'Editor');
  const owner = hasRole(folder.myRole, 'Owner');
  const dialog = (name: Open) => (next: boolean) => {
    if (!next) {
      move.reset();
      remove.reset();
    }
    setOpen(next ? name : null);
  };

  const subfolders = children.data?.pages.flatMap((page) => page.items) ?? [];
  const items = automations.data?.pages.flatMap((page) => page.items) ?? [];
  const loading = children.isPending || automations.isPending;
  const failed = children.error ?? automations.error;

  const actions: Action[] = [
    ...(editor ? [{ label: 'Rename', Icon: Pencil, onSelect: () => setOpen('rename') }] : []),
    ...(editor ? [{ label: 'Move', Icon: FolderInput, onSelect: () => setOpen('move') }] : []),
    ...(owner ? [{ label: 'Delete', Icon: Trash2, onSelect: () => setOpen('delete'), danger: true }] : []),
  ];

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-6 p-6">
      <PageHeader
        title={folder.name}
        badges={
          <>
            <Badge>{folder.myRole}</Badge>
            {folder.isRestricted && (
              <Badge tone="warn">
                <Lock aria-hidden="true" size={11} className="mr-1" />
                Restricted
              </Badge>
            )}
          </>
        }
        actions={
          <>
            {editor && (
              <>
                <Button onClick={() => setOpen('folder')}>
                  <FolderPlus aria-hidden="true" size={16} />
                  New folder
                </Button>
                <Button variant="primary" onClick={() => setOpen('automation')}>
                  <Plus aria-hidden="true" size={16} />
                  New automation
                </Button>
              </>
            )}
            <ActionsMenu label="Folder actions" actions={actions} />
          </>
        }
      >
        {folder.description}
      </PageHeader>

      <TabLinks
        label="Folder"
        tabs={[
          { to: `/folders/${folder.id}`, label: 'Contents', end: true },
          { to: `/folders/${folder.id}/configuration`, label: 'Configuration' },
        ]}
      />
      {configuring ? (
        <FolderConfiguration folder={folder} />
      ) : (
        <>
          {loading && <Loading label="Loading the folder's content…" />}
          {failed && <ErrorAlert error={failed} title="The content could not be loaded" />}
          {!loading && !failed && subfolders.length === 0 && items.length === 0 && (
            <EmptyState
              Icon={FolderOpen}
              title="This folder is empty"
              action={
                editor ? (
                  <Button variant="primary" onClick={() => setOpen('automation')}>
                    <Plus aria-hidden="true" size={16} />
                    New automation
                  </Button>
                ) : undefined
              }
            >
              {editor
                ? 'Create an automation or a subfolder in it.'
                : 'Automations and subfolders appear here once someone with the Editor role adds them.'}
            </EmptyState>
          )}
          {(subfolders.length > 0 || items.length > 0) && (
            <ul aria-label="Content" className="divide-y divide-line rounded-panel border border-line bg-surface">
              {subfolders.map((child) => (
                <FolderRow key={child.id} folder={child} />
              ))}
              {children.hasNextPage && (
                <MoreRow
                  label="Show more folders"
                  busy={children.isFetchingNextPage}
                  onClick={() => void children.fetchNextPage()}
                />
              )}
              {items.map((automation) => (
                <AutomationRow key={automation.id} automation={automation} />
              ))}
              {automations.hasNextPage && (
                <MoreRow
                  label="Show more automations"
                  busy={automations.isFetchingNextPage}
                  onClick={() => void automations.fetchNextPage()}
                />
              )}
            </ul>
          )}
        </>
      )}

      <CreateFolderDialog parent={folder} open={open === 'folder'} onOpenChange={dialog('folder')} />
      <CreateAutomationDialog folder={folder} open={open === 'automation'} onOpenChange={dialog('automation')} />
      <EditFolderDialog folder={folder} tag={tag} open={open === 'rename'} onOpenChange={dialog('rename')} />
      <MoveDialog
        title={`Move ${folder.name}`}
        open={open === 'move'}
        onOpenChange={dialog('move')}
        currentFolderId={folder.parentId}
        excludeId={folder.id}
        allowTop={owner && folder.parentId !== null}
        busy={move.isPending}
        error={move.error}
        onMove={(target) =>
          move.mutate({ parentId: target === 'top' ? null : target.id, tag }, { onSuccess: () => setOpen(null) })
        }
      />
      <ConfirmDialog
        title={`Delete ${folder.name}?`}
        open={open === 'delete'}
        onOpenChange={dialog('delete')}
        confirmLabel="Delete folder"
        busy={remove.isPending}
        error={remove.error}
        onConfirm={() =>
          remove.mutate(
            { tag },
            {
              onSuccess: () => void navigate(folder.parentId ? `/folders/${folder.parentId}` : '/', { replace: true }),
            },
          )
        }
      >
        Only an empty folder can be deleted. Its grants go with it, and runs left in it are deleted too. This cannot be
        undone.
      </ConfirmDialog>
    </div>
  );
}

function MoreRow({ label, busy, onClick }: { label: string; busy: boolean; onClick: () => void }) {
  return (
    <li className="px-4 py-2">
      <Button variant="ghost" busy={busy} onClick={onClick}>
        {label}
      </Button>
    </li>
  );
}
