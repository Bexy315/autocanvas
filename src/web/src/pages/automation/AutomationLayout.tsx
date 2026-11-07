import { Download, FolderInput, Pencil, Play, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { Outlet, useNavigate, useParams } from 'react-router';
import { downloadUrl, useDeleteAutomation, useMoveAutomation } from '../../api/automations';
import { RenameAutomationDialog } from '../../dialogs/AutomationDialogs';
import { ConfirmDialog } from '../../dialogs/ConfirmDialog';
import { MoveDialog } from '../../dialogs/MoveDialog';
import { usePageInfo } from '../../layout/pageContext';
import { hasRole } from '../../lib/roles';
import { ErrorAlert } from '../../ui/Alert';
import { ActionsMenu, type Action } from '../../ui/ActionsMenu';
import { Badge } from '../../ui/Badge';
import { Button } from '../../ui/Button';
import { PageHeader } from '../../ui/PageHeader';
import { Loading } from '../../ui/Spinner';
import { TabLinks } from '../../ui/Tabs';
import { folderPageInfo } from '../folderPageInfo';
import type { AutomationContext } from './context';
import { useAutomationPage } from './useAutomationPage';
import { useStartByHand } from './useStartByHand';

type Open = 'rename' | 'move' | 'delete' | null;

/** An automation's page: its title, actions and tabs; the tabs get the automation as outlet context. */
export function AutomationLayout() {
  const { id = '' } = useParams();
  const { context, failed, error } = useAutomationPage(id);
  usePageInfo(
    context
      ? { ...folderPageInfo(context.folder, context.automation.title), automationId: context.automation.id }
      : null,
  );

  if (failed) {
    return (
      <div className="mx-auto max-w-5xl p-6">
        <ErrorAlert error={error} title="The automation could not be loaded" />
      </div>
    );
  }
  if (!context) {
    return <Loading label="Loading the automation…" />;
  }
  return <AutomationPage key={id} context={context} />;
}

function AutomationPage({ context }: { context: AutomationContext }) {
  const { automation, tag, folder } = context;
  const [open, setOpen] = useState<Open>(null);
  const navigate = useNavigate();
  const move = useMoveAutomation(automation.id);
  const remove = useDeleteAutomation(automation.id);
  const run = useStartByHand(automation);
  // A repository automation is renamed, moved and deleted in its repository, not here.
  const editor = hasRole(automation.myRole, 'Editor') && !automation.repository;
  const dialog = (name: Open) => (next: boolean) => {
    if (!next) {
      move.reset();
      remove.reset();
    }
    setOpen(next ? name : null);
  };

  const actions: Action[] = [
    { label: 'Download', Icon: Download, href: downloadUrl(automation.id) },
    ...(editor
      ? [
          { label: 'Rename', Icon: Pencil, onSelect: () => setOpen('rename') },
          { label: 'Move', Icon: FolderInput, onSelect: () => setOpen('move') },
          { label: 'Delete', Icon: Trash2, onSelect: () => setOpen('delete'), danger: true },
        ]
      : []),
  ];

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-5 p-6">
      <PageHeader
        title={automation.title}
        badges={
          <>
            <Badge tone="accent">Version {automation.currentVersion}</Badge>
            {automation.repository && <Badge>From {automation.repository.repositoryName}</Badge>}
          </>
        }
        actions={
          <>
            {run.canStart && (
              <Button variant="primary" busy={run.busy} onClick={run.start}>
                <Play aria-hidden="true" size={14} />
                Run
              </Button>
            )}
            <ActionsMenu label="Automation actions" actions={actions} />
          </>
        }
      >
        {automation.name !== automation.title && <code className="font-mono text-xs">{automation.name}</code>}
        {automation.description && <p className="mt-1 text-ink">{automation.description}</p>}
      </PageHeader>
      {run.error && <ErrorAlert error={run.error} title={run.errorTitle} />}

      <TabLinks
        label="Automation"
        tabs={[
          { to: `/automations/${automation.id}`, label: 'Overview', end: true },
          { to: `/automations/${automation.id}/edit`, label: 'Editor' },
          { to: `/automations/${automation.id}/runs`, label: 'Runs' },
          { to: `/automations/${automation.id}/triggers`, label: 'Triggers' },
          { to: `/automations/${automation.id}/configuration`, label: 'Configuration' },
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
