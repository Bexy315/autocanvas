import { Download, FolderInput, Pencil, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { Outlet, useNavigate, useParams } from 'react-router';
import { downloadUrl, useAutomation, useDeleteAutomation, useMoveAutomation } from '../../api/automations';
import { useFolder } from '../../api/folders';
import { RenameAutomationDialog } from '../../dialogs/AutomationDialogs';
import { ConfirmDialog } from '../../dialogs/ConfirmDialog';
import { MoveDialog } from '../../dialogs/MoveDialog';
import { usePageInfo } from '../../layout/pageContext';
import { hasRole } from '../../lib/roles';
import { ErrorAlert } from '../../ui/Alert';
import { ActionsMenu, type Action } from '../../ui/ActionsMenu';
import { Badge } from '../../ui/Badge';
import { PageHeader } from '../../ui/PageHeader';
import { Loading } from '../../ui/Spinner';
import { TabLinks } from '../../ui/Tabs';
import { folderPageInfo } from '../folderPageInfo';
import type { AutomationContext } from './context';

type Open = 'rename' | 'move' | 'delete' | null;

/** An automation's page: its title, actions and tabs; the tabs get the automation as outlet context. */
export function AutomationLayout() {
  const { id = '' } = useParams();
  const automation = useAutomation(id);
  const folderId = automation.data?.data.folderId ?? '';
  const folder = useFolder(folderId);
  const ready = automation.data && folder.data;
  usePageInfo(ready ? folderPageInfo(folder.data.data, automation.data.data.title) : null);

  if (automation.isError || folder.isError) {
    return (
      <div className="mx-auto max-w-5xl p-6">
        <ErrorAlert error={automation.error ?? folder.error} title="The automation could not be loaded" />
      </div>
    );
  }
  if (!ready) {
    return <Loading label="Loading the automation…" />;
  }
  return (
    <AutomationPage
      key={id}
      context={{ automation: automation.data.data, tag: automation.data.tag, folder: folder.data.data }}
    />
  );
}

function AutomationPage({ context }: { context: AutomationContext }) {
  const { automation, tag, folder } = context;
  const [open, setOpen] = useState<Open>(null);
  const navigate = useNavigate();
  const move = useMoveAutomation(automation.id);
  const remove = useDeleteAutomation(automation.id);
  const editor = hasRole(automation.myRole, 'Editor');
  const dialog = (name: Open) => (next: boolean) => {
    if (!next) {
      move.reset();
      remove.reset();
    }
    setOpen(next ? name : null);
  };

  const actions: Action[] = editor
    ? [
        { label: 'Rename', Icon: Pencil, onSelect: () => setOpen('rename') },
        { label: 'Move', Icon: FolderInput, onSelect: () => setOpen('move') },
        { label: 'Delete', Icon: Trash2, onSelect: () => setOpen('delete'), danger: true },
      ]
    : [];

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-5 p-6">
      <PageHeader
        title={automation.title}
        badges={<Badge tone="accent">Version {automation.currentVersion}</Badge>}
        actions={
          <>
            <a
              href={downloadUrl(automation.id)}
              download
              className="inline-flex h-8 items-center gap-1.5 rounded-control border border-line-strong bg-surface px-3 text-sm font-medium hover:bg-sunken"
            >
              <Download aria-hidden="true" size={16} />
              Download
            </a>
            <ActionsMenu label="Automation actions" actions={actions} />
          </>
        }
      >
        {automation.name !== automation.title && <code className="font-mono text-xs">{automation.name}</code>}
        {automation.description && <p className="mt-1 text-ink">{automation.description}</p>}
      </PageHeader>

      <TabLinks
        label="Automation"
        tabs={[
          { to: `/automations/${automation.id}`, label: 'Overview', end: true },
          { to: `/automations/${automation.id}/versions`, label: 'Versions' },
        ]}
      />
      <Outlet context={context} />

      <RenameAutomationDialog
        automation={automation}
        tag={tag}
        open={open === 'rename'}
        onOpenChange={dialog('rename')}
      />
      <MoveDialog
        title={`Move ${automation.title}`}
        open={open === 'move'}
        onOpenChange={dialog('move')}
        currentFolderId={folder.id}
        allowTop={false}
        busy={move.isPending}
        error={move.error}
        onMove={(target) =>
          target !== 'top' && move.mutate({ folderId: target.id, tag }, { onSuccess: () => setOpen(null) })
        }
      />
      <ConfirmDialog
        title={`Delete ${automation.title}?`}
        open={open === 'delete'}
        onOpenChange={dialog('delete')}
        confirmLabel="Delete automation"
        busy={remove.isPending}
        error={remove.error}
        onConfirm={() =>
          remove.mutate({ tag }, { onSuccess: () => void navigate(`/folders/${folder.id}`, { replace: true }) })
        }
      >
        This deletes the automation with all {automation.currentVersion} versions; it cannot be undone. Its runs stay
        readable, each with the file it ran.
      </ConfirmDialog>
    </div>
  );
}
