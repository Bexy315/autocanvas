/**
 * The runners: whether each is online, what it offers, pausing, renaming and deleting; and
 * registering a new one with a one-time token and the commands that use it.
 */
import { clsx } from 'clsx';
import { Pause, Pencil, Play, Plus, Server, Trash2 } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import {
  useCreateRegistrationToken,
  useDeleteRegistrationToken,
  useDeleteRunner,
  useRegistrationTokens,
  useRunners,
  useUpdateRunner,
  type RunnerView,
} from '../../api/admin';
import { useInstanceSettings } from '../../api/instance';
import { ConfirmDialog } from '../../dialogs/ConfirmDialog';
import { guessRunnerAddress } from '../../lib/runnerAddress';
import { ActionsMenu } from '../../ui/ActionsMenu';
import { ErrorAlert } from '../../ui/Alert';
import { Badge } from '../../ui/Badge';
import { Button } from '../../ui/Button';
import { Dialog, DialogActions } from '../../ui/Dialog';
import { EmptyState } from '../../ui/EmptyState';
import { RelativeTime } from '../../ui/RelativeTime';
import { CopyButton, CopyValue, OnceDialog } from '../../ui/SecretOnce';
import { Loading } from '../../ui/Spinner';
import { TextField } from '../../ui/TextField';

type Open = { kind: 'rename' | 'delete'; runner: RunnerView } | null;

export function RunnersAdmin() {
  const runners = useRunners();
  const instance = useInstanceSettings();
  const [registering, setRegistering] = useState(false);
  const [token, setToken] = useState<string | null>(null);
  const [open, setOpen] = useState<Open>(null);
  const items = runners.data?.pages.flatMap((page) => page.items) ?? [];

  return (
    <div className="flex flex-col gap-4">
      <div className="flex justify-end">
        <Button variant="primary" onClick={() => setRegistering(true)}>
          <Plus aria-hidden="true" size={14} />
          Register a runner
        </Button>
      </div>
      {runners.isPending && <Loading label="Loading runners…" />}
      {runners.isError && <ErrorAlert error={runners.error} title="The runners could not be loaded" />}
      {runners.data && items.length === 0 && (
        <EmptyState Icon={Server} title="No runners">
          Runners run the jobs. Register one on a machine with Docker.
        </EmptyState>
      )}
      {items.length > 0 && (
        <ul aria-label="Runners" className="divide-y divide-line rounded-panel border border-line bg-surface">
          {items.map((runner) => (
            <RunnerRow key={runner.id} runner={runner} onOpen={setOpen} />
          ))}
        </ul>
      )}
      <RegistrationTokens />

      {registering && (
        <RegisterDialog
          onClose={() => setRegistering(false)}
          onCreated={(created) => {
            setRegistering(false);
            setToken(created);
          }}
        />
      )}
      {token && (
        <TokenDialog
          token={token}
          address={instance.data?.runnerAddress.value?.replace(/\/$/, '') ?? guessRunnerAddress()}
          onClose={() => setToken(null)}
        />
      )}
      {open?.kind === 'rename' && <RenameDialog runner={open.runner} onClose={() => setOpen(null)} />}
      {open?.kind === 'delete' && <DeleteRunnerDialog runner={open.runner} onClose={() => setOpen(null)} />}
    </div>
  );
}

function RunnerRow({ runner, onOpen }: { runner: RunnerView; onOpen: (open: Open) => void }) {
  const update = useUpdateRunner(runner.id);
  const capabilities = runner.capabilities;
  return (
    <li className="flex flex-col gap-1 px-4 py-2.5">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <span
          aria-hidden="true"
          className={clsx('size-2.5 rounded-full', runner.isOnline ? 'bg-ok' : 'bg-line-strong')}
        />
        <span className="font-medium">{runner.name}</span>
        <Badge tone={runner.isOnline ? 'ok' : 'neutral'}>{runner.isOnline ? 'Online' : 'Offline'}</Badge>
        {runner.isPaused && <Badge tone="warn">Paused</Badge>}
        <span className="ml-auto text-xs text-muted">
          {runner.lastSeenAt ? (
            <>
              seen <RelativeTime value={runner.lastSeenAt} />
            </>
          ) : (
            'never connected'
          )}
        </span>
        <Button busy={update.isPending} onClick={() => update.mutate({ isPaused: !runner.isPaused })}>
          {runner.isPaused ? <Play aria-hidden="true" size={14} /> : <Pause aria-hidden="true" size={14} />}
          {runner.isPaused ? 'Resume' : 'Pause'}
        </Button>
        <ActionsMenu
          label={`Actions for ${runner.name}`}
          actions={[
            { label: 'Rename', Icon: Pencil, onSelect: () => onOpen({ kind: 'rename', runner }) },
            { label: 'Delete', Icon: Trash2, onSelect: () => onOpen({ kind: 'delete', runner }), danger: true },
          ]}
        />
      </div>
      {capabilities && (
        <p className="text-xs text-muted">
          {[
            capabilities.labels.length > 0 ? `labels ${capabilities.labels.join(', ')}` : null,
            capabilities.executor,
            `${capabilities.os}/${capabilities.arch}`,
            `${capabilities.maxConcurrentJobs} at a time`,
            capabilities.runnerVersion ? `version ${capabilities.runnerVersion}` : null,
          ]
            .filter(Boolean)
            .join(' · ')}
        </p>
      )}
      {update.isError && <ErrorAlert error={update.error} title="The runner did not change" />}
    </li>
  );
}

const expiries = [
  { days: 1, label: 'in a day' },
  { days: 7, label: 'in a week' },
  { days: 30, label: 'in 30 days' },
];

function RegisterDialog({ onClose, onCreated }: { onClose: () => void; onCreated: (token: string) => void }) {
  const create = useCreateRegistrationToken();
  const [description, setDescription] = useState('');
  const [days, setDays] = useState(1);
  const submit = (event: FormEvent) => {
    event.preventDefault();
    create.mutate(
      {
        description: description.trim() === '' ? null : description.trim(),
        expiresAt: new Date(Date.now() + days * 24 * 3600 * 1000).toISOString(),
      },
      { onSuccess: (created) => onCreated(created.token) },
    );
  };
  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onClose()}
      title="Register a runner"
      description="A one-time token for the runner's register command."
    >
      <form onSubmit={submit} className="flex flex-col gap-4" noValidate>
        {create.isError && <ErrorAlert error={create.error} />}
        <TextField
          label="What it is for"
          value={description}
          placeholder="build box in the basement"
          onChange={(event) => setDescription(event.target.value)}
        />
        <label className="flex flex-col gap-1 text-sm">
          <span className="font-medium">Usable until</span>
          <select
            value={days}
            onChange={(event) => setDays(Number(event.target.value))}
            className="h-9 rounded-control border border-line-strong bg-surface px-2"
          >
            {expiries.map((entry) => (
              <option key={entry.days} value={entry.days}>
                {entry.label}
              </option>
            ))}
          </select>
        </label>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="primary" busy={create.isPending}>
            Create token
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}

function TokenDialog({ token, address, onClose }: { token: string; address: string; onClose: () => void }) {
  const [server, setServer] = useState(address);
  const docker = [
    'docker run --rm -it \\',
    '  -v /var/lib/autocanvas-runner:/var/lib/autocanvas-runner \\',
    `  -e AUTOCANVAS_RUNNER__SERVER=${server} \\`,
    `  autocanvas-runner register --token ${token} --name build-1`,
  ].join('\n');
  const dotnet = [
    `$env:AUTOCANVAS_RUNNER__SERVER = '${server}'`,
    `dotnet AutoCanvas.Runner.dll register --token ${token} --name build-1`,
  ].join('\n');
  return (
    <OnceDialog title="Runner token created" saveLabel="I have used or saved the token" onClose={onClose} wide>
      <CopyValue label="Token" value={token} />
      <TextField
        label="The server's runner address, as the runner reaches it"
        value={server}
        hint="Filled in from Administration → Instance, where it is set once for every new runner."
        onChange={(event) => setServer(event.target.value)}
      />
      {[
        { title: 'With Docker', text: docker },
        { title: 'With .NET (PowerShell)', text: dotnet },
      ].map((example) => (
        <div key={example.title} className="flex flex-col gap-1">
          <div className="flex items-center justify-between">
            <span className="text-sm font-medium">{example.title}</span>
            <CopyButton text={example.text} label={`Copy the command ${example.title.toLowerCase()}`} />
          </div>
          <pre className="overflow-x-auto rounded-control border border-line bg-sunken px-3 py-2 font-mono text-xs leading-5">
            {example.text}
          </pre>
        </div>
      ))}
      <p className="text-xs text-muted">
        Then start the runner the same way without the register command; see Runners in the documentation.
      </p>
    </OnceDialog>
  );
}

function RenameDialog({ runner, onClose }: { runner: RunnerView; onClose: () => void }) {
  const update = useUpdateRunner(runner.id);
  const [name, setName] = useState(runner.name);
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()} title={`Rename ${runner.name}`}>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          update.mutate({ name: name.trim() }, { onSuccess: onClose });
        }}
        className="flex flex-col gap-4"
        noValidate
      >
        {update.isError && <ErrorAlert error={update.error} />}
        <TextField label="Name" value={name} onChange={(event) => setName(event.target.value)} />
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="primary" busy={update.isPending} disabled={name.trim() === ''}>
            Rename
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}

function DeleteRunnerDialog({ runner, onClose }: { runner: RunnerView; onClose: () => void }) {
  const remove = useDeleteRunner(runner.id);
  return (
    <ConfirmDialog
      title={`Delete ${runner.name}?`}
      open
      onOpenChange={(open) => !open && onClose()}
      confirmLabel="Delete runner"
      busy={remove.isPending}
      error={remove.isError ? remove.error : null}
      onConfirm={() => remove.mutate(undefined, { onSuccess: onClose })}
    >
      Its credential stops working and a connected runner is told not to reconnect. Jobs it still has are offered to
      other runners or fail, as their retry settings say.
    </ConfirmDialog>
  );
}

function RegistrationTokens() {
  const tokens = useRegistrationTokens();
  const remove = useDeleteRegistrationToken();
  const items = tokens.data ?? [];
  if (items.length === 0) {
    return null;
  }
  return (
    <section aria-label="Registration tokens" className="rounded-panel border border-line bg-surface">
      <h3 className="border-b border-line px-4 py-2.5 font-semibold">Registration tokens</h3>
      {remove.isError && <ErrorAlert error={remove.error} />}
      <ul className="divide-y divide-line">
        {items.map((token) => (
          <li key={token.id} className="flex flex-wrap items-center gap-3 px-4 py-2 text-sm">
            <code className="font-mono text-xs">{token.hint}</code>
            <span className="min-w-0 flex-1 truncate text-muted">{token.description}</span>
            <Badge tone={token.isUsable ? 'accent' : 'neutral'}>
              {token.usedAt ? 'Used' : token.isUsable ? 'Open' : 'Expired'}
            </Badge>
            <span className="text-xs text-muted">
              created <RelativeTime value={token.createdAt} />
            </span>
            {token.isUsable && (
              <button
                type="button"
                aria-label={`Delete the token ${token.hint}`}
                title="Delete"
                onClick={() => remove.mutate(token.id)}
                className="rounded-control p-1.5 text-muted hover:bg-danger-soft hover:text-danger"
              >
                <Trash2 aria-hidden="true" size={14} />
              </button>
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}
