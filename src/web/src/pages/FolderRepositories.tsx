import { GitBranch, Link2, Pencil, Plus, RefreshCw, Trash2, Unplug, Webhook } from 'lucide-react';
import { useState } from 'react';
import type { FolderView } from '../api/folders';
import {
  useCreateRepositoryWebhook,
  useDeleteRepositoryWebhook,
  useRepositories,
  useSyncRepository,
  type CreatedRepositoryWebhook,
  type RepositoryView,
} from '../api/repositories';
import { useMe } from '../api/session';
import {
  ChangeRepositoryDialog,
  ConnectRepositoryDialog,
  DisconnectRepositoryDialog,
  WebhookCreatedDialog,
} from '../dialogs/RepositoryDialogs';
import { hasRole } from '../lib/roles';
import { ActionsMenu, type Action } from '../ui/ActionsMenu';
import { Alert, ErrorAlert } from '../ui/Alert';
import { Badge } from '../ui/Badge';
import { Button } from '../ui/Button';
import { EmptyState } from '../ui/EmptyState';
import { RelativeTime } from '../ui/RelativeTime';
import { Loading } from '../ui/Spinner';

type Open = { kind: 'change' | 'disconnect'; repository: RepositoryView } | null;

/** The repositories connected in a folder (ADR-0021): their state, and for editors what to do with them. */
export function FolderRepositories({ folder }: { folder: FolderView }) {
  const repositories = useRepositories(folder.id);
  const me = useMe();
  const editor = hasRole(folder.myRole, 'Editor');
  const [connecting, setConnecting] = useState(false);
  const [open, setOpen] = useState<Open>(null);
  const [created, setCreated] = useState<CreatedRepositoryWebhook | null>(null);

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-2">
        <p className="flex-1 text-sm text-muted">
          The files <code>.autocanvas/automations/*.yml</code> of these repositories are automations of this folder,
          changed in the repository and read-only here.
        </p>
        {editor && (
          <Button variant="primary" onClick={() => setConnecting(true)}>
            <Plus aria-hidden="true" size={14} />
            Connect a repository
          </Button>
        )}
      </div>
      {repositories.isPending && <Loading label="Loading the repositories…" />}
      {repositories.isError && <ErrorAlert error={repositories.error} title="The repositories could not be loaded" />}
      {repositories.data?.length === 0 && (
        <EmptyState Icon={GitBranch} title="No repositories">
          Connect a GitHub repository through the GitHub App, or any Git repository by its address, to keep automations
          next to the code they build.
        </EmptyState>
      )}
      {repositories.data && repositories.data.length > 0 && (
        <ul aria-label="Repositories" className="flex flex-col gap-3">
          {repositories.data.map((repository) => (
            <RepositoryCard
              key={repository.id}
              repository={repository}
              role={folder.myRole}
              onOpen={setOpen}
              onWebhook={setCreated}
            />
          ))}
        </ul>
      )}
      {connecting && (
        <ConnectRepositoryDialog
          folderId={folder.id}
          isAdmin={me.data?.user.isAdmin ?? false}
          onClose={() => setConnecting(false)}
        />
      )}
      {open?.kind === 'change' && <ChangeRepositoryDialog repository={open.repository} onClose={() => setOpen(null)} />}
      {open?.kind === 'disconnect' && (
        <DisconnectRepositoryDialog repository={open.repository} onClose={() => setOpen(null)} />
      )}
      {created && <WebhookCreatedDialog created={created} onClose={() => setCreated(null)} />}
    </div>
  );
}

function RepositoryCard({
  repository,
  role,
  onOpen,
  onWebhook,
}: {
  repository: RepositoryView;
  role: FolderView['myRole'];
  onOpen: (open: Open) => void;
  onWebhook: (created: CreatedRepositoryWebhook) => void;
}) {
  const sync = useSyncRepository(repository);
  const createWebhook = useCreateRepositoryWebhook(repository);
  const deleteWebhook = useDeleteRepositoryWebhook(repository);
  const editor = hasRole(role, 'Editor');
  const operator = hasRole(role, 'Operator');
  const github = repository.kind === 'GitHub';
  const actions: Action[] = editor
    ? [
        { label: 'Change', Icon: Pencil, onSelect: () => onOpen({ kind: 'change', repository }) },
        ...(github
          ? []
          : [
              {
                label: repository.webhook ? 'New webhook address' : 'Create webhook address',
                Icon: Webhook,
                onSelect: () => createWebhook.mutate(undefined, { onSuccess: onWebhook }),
              },
              ...(repository.webhook
                ? [{ label: 'Delete webhook address', Icon: Trash2, onSelect: () => deleteWebhook.mutate() }]
                : []),
            ]),
        { label: 'Disconnect', Icon: Unplug, onSelect: () => onOpen({ kind: 'disconnect', repository }), danger: true },
      ]
    : [];
  const commit = repository.lastCommit?.slice(0, 7);

  return (
    <li className="flex flex-col gap-2 rounded-panel border border-line bg-surface p-4">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <GitBranch aria-hidden="true" size={16} className="text-muted" />
        <h2 className="font-semibold">
          {repository.webUrl ? (
            <a href={repository.webUrl} target="_blank" rel="noreferrer" className="hover:underline">
              {repository.name}
            </a>
          ) : (
            repository.name
          )}
        </h2>
        <Badge tone={github ? 'accent' : 'neutral'}>{github ? 'GitHub' : 'Git'}</Badge>
        <span className="text-xs text-muted">
          {repository.automations === 1 ? '1 automation' : `${repository.automations} automations`}
        </span>
        <span className="ml-auto flex items-center gap-2">
          {operator && (
            <Button busy={sync.isPending} onClick={() => sync.mutate()}>
              <RefreshCw aria-hidden="true" size={14} />
              Sync
            </Button>
          )}
          {actions.length > 0 && <ActionsMenu label={`Actions for ${repository.name}`} actions={actions} />}
        </span>
      </div>
      <dl className="grid grid-cols-[max-content_1fr] gap-x-4 gap-y-0.5 text-sm">
        <dt className="text-muted">Address</dt>
        <dd className="truncate font-mono text-xs leading-5">{repository.url}</dd>
        <dt className="text-muted">Branch</dt>
        <dd>
          {repository.branch ?? repository.defaultBranch ?? '–'}
          {commit && (
            <>
              {' at '}
              {repository.webUrl ? (
                <a
                  href={`${repository.webUrl}/commit/${repository.lastCommit}`}
                  target="_blank"
                  rel="noreferrer"
                  className="font-mono text-xs hover:underline"
                >
                  {commit}
                </a>
              ) : (
                <code className="text-xs">{commit}</code>
              )}
            </>
          )}
          {repository.lastSyncedAt && (
            <span className="text-muted">
              , read <RelativeTime value={repository.lastSyncedAt} />
            </span>
          )}
        </dd>
        <dt className="text-muted">Pushes</dt>
        <dd>
          {[
            Number(repository.pollMinutes) > 0 ? `polled every ${repository.pollMinutes} min` : 'not polled',
            github ? 'events of the GitHub App' : repository.webhook ? `webhook ${repository.webhook.hint}` : null,
          ]
            .filter(Boolean)
            .join(', ')}
          {!github && !repository.webhook && Number(repository.pollMinutes) === 0 && (
            <span className="text-warn"> – no pushes arrive</span>
          )}
        </dd>
      </dl>
      {sync.isError && <ErrorAlert error={sync.error} title="The repository was not read" />}
      {(createWebhook.isError || deleteWebhook.isError) && (
        <ErrorAlert error={createWebhook.error ?? deleteWebhook.error} title="The webhook address was not changed" />
      )}
      {repository.lastError && (
        <Alert tone="danger" title="The last read failed">
          {repository.lastError}
        </Alert>
      )}
      {repository.problems.length > 0 && (
        <details className="text-sm">
          <summary className="cursor-pointer text-warn">
            {repository.problems.length === 1
              ? '1 file is not an automation'
              : `${repository.problems.length} problems in files`}
          </summary>
          <ul aria-label={`Problems of ${repository.name}`} className="mt-2 flex flex-col gap-1 pl-4">
            {repository.problems.map((problem) => (
              <li key={`${problem.path}:${problem.line ?? ''}:${problem.message}`}>
                <Link2 aria-hidden="true" size={12} className="mr-1 inline text-muted" />
                <code className="text-xs">
                  {problem.path}
                  {problem.line ? `:${problem.line}` : ''}
                </code>{' '}
                {problem.message}
              </li>
            ))}
          </ul>
        </details>
      )}
    </li>
  );
}
