import { GitBranch, Search } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { Link } from 'react-router';
import { ApiError } from '../api/problem';
import {
  useConnectGitHubRepository,
  useConnectRepository,
  useDisconnectRepository,
  useGitHubRepositories,
  useUpdateRepository,
  type CreatedRepositoryWebhook,
  type RepositoryView,
} from '../api/repositories';
import { ErrorAlert } from '../ui/Alert';
import { Button } from '../ui/Button';
import { Dialog, DialogActions } from '../ui/Dialog';
import { CopyValue, OnceDialog } from '../ui/SecretOnce';
import { Loading } from '../ui/Spinner';
import { TextField } from '../ui/TextField';
import { ConfirmDialog } from './ConfirmDialog';

type Way = 'github' | 'git';

/** Connects a repository in a folder: one the GitHub App reaches, or any Git repository by its address. */
export function ConnectRepositoryDialog({
  folderId,
  isAdmin,
  onClose,
}: {
  folderId: string;
  isAdmin: boolean;
  onClose: () => void;
}) {
  const [way, setWay] = useState<Way>('github');
  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onClose()}
      title="Connect a repository"
      description="Its .autocanvas/automations/*.yml files become automations of this folder."
      wide
    >
      <div className="flex flex-col gap-4">
        <fieldset className="flex gap-1 rounded-control bg-sunken p-1">
          <legend className="sr-only">Where the repository is</legend>
          {(
            [
              ['github', 'GitHub'],
              ['git', 'Git address'],
            ] as const
          ).map(([value, label]) => (
            <button
              key={value}
              type="button"
              aria-pressed={way === value}
              onClick={() => setWay(value)}
              className={`flex-1 rounded-control px-3 py-1.5 text-sm font-medium ${way === value ? 'bg-surface text-ink shadow-sm' : 'text-muted hover:text-ink'}`}
            >
              {label}
            </button>
          ))}
        </fieldset>
        {way === 'github' ? (
          <GitHubChoice folderId={folderId} isAdmin={isAdmin} onClose={onClose} />
        ) : (
          <GitAddressForm folderId={folderId} onClose={onClose} />
        )}
      </div>
    </Dialog>
  );
}

function GitHubChoice({ folderId, isAdmin, onClose }: { folderId: string; isAdmin: boolean; onClose: () => void }) {
  const repositories = useGitHubRepositories(folderId, true);
  const connect = useConnectGitHubRepository(folderId);
  const [filter, setFilter] = useState('');
  if (repositories.isPending) {
    return <Loading label="Asking GitHub…" />;
  }
  if (repositories.isError) {
    return (
      <div className="flex flex-col gap-2">
        <ErrorAlert error={repositories.error} title="GitHub cannot be asked for repositories" />
        <p className="text-sm text-muted">
          {isAdmin ? (
            <>
              Set up the GitHub App under <Link to="/admin/forges">Administration, Forges</Link>, and install it on the
              account with the repository.
            </>
          ) : (
            'An admin sets up the GitHub App under Administration, Forges. Until then, connect a repository by its Git address.'
          )}
        </p>
      </div>
    );
  }
  const shown = repositories.data.filter((r) => r.fullName.toLowerCase().includes(filter.trim().toLowerCase()));
  return (
    <div className="flex flex-col gap-3">
      <label className="flex items-center gap-2 rounded-control border border-line-strong bg-surface px-2.5">
        <Search aria-hidden="true" size={14} className="text-muted" />
        <span className="sr-only">Find a repository</span>
        <input
          value={filter}
          onChange={(event) => setFilter(event.target.value)}
          placeholder="Find a repository"
          className="h-9 flex-1 bg-transparent text-sm outline-none"
        />
      </label>
      {connect.isError && <ErrorAlert error={connect.error} title="The repository was not connected" />}
      {repositories.data.length === 0 ? (
        <p className="text-sm text-muted">
          The app is installed nowhere yet, or on no repository. Install it on GitHub, then come back.
        </p>
      ) : (
        <ul
          aria-label="Repositories on GitHub"
          className="max-h-80 divide-y divide-line overflow-auto rounded-panel border border-line"
        >
          {shown.map((repository) => (
            <li key={repository.id} className="flex items-center gap-3 px-3 py-2">
              <GitBranch aria-hidden="true" size={14} className="text-muted" />
              <span className="flex-1 truncate font-medium">{repository.fullName}</span>
              {repository.private && <span className="text-xs text-muted">private</span>}
              <Button
                busy={connect.isPending && connect.variables.repository === repository.fullName}
                disabled={connect.isPending}
                onClick={() =>
                  connect.mutate(
                    { installationId: repository.installationId, repository: repository.fullName },
                    { onSuccess: onClose },
                  )
                }
              >
                Connect
              </Button>
            </li>
          ))}
          {shown.length === 0 && <li className="px-3 py-2 text-sm text-muted">No repository matches.</li>}
        </ul>
      )}
    </div>
  );
}

function GitAddressForm({ folderId, onClose }: { folderId: string; onClose: () => void }) {
  const connect = useConnectRepository(folderId);
  const [address, setAddress] = useState('');
  const [name, setName] = useState('');
  const [defaultBranch, setDefaultBranch] = useState('');
  const [userName, setUserName] = useState('');
  const [secret, setSecret] = useState('');
  const [pollMinutes, setPollMinutes] = useState('5');
  const errors = (field: string) => (connect.error instanceof ApiError ? connect.error.fieldErrors(field) : []);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    connect.mutate(
      {
        address: address.trim(),
        name: name.trim() || null,
        defaultBranch: defaultBranch.trim() || null,
        userName: userName.trim() || null,
        secret: secret || null,
        pollMinutes: Number(pollMinutes),
      },
      { onSuccess: onClose },
    );
  };

  return (
    <form onSubmit={submit} className="flex flex-col gap-3" noValidate>
      <TextField
        label="Address"
        placeholder="https://git.example.org/team/shop.git"
        hint="The clone address over https (or http on a trusted network)."
        value={address}
        errors={errors('address')}
        onChange={(event) => setAddress(event.target.value)}
      />
      <div className="grid gap-3 sm:grid-cols-2">
        <TextField
          label="Name"
          placeholder="From the address"
          value={name}
          errors={errors('name')}
          onChange={(event) => setName(event.target.value)}
        />
        <TextField
          label="Branch"
          placeholder="The repository's default"
          value={defaultBranch}
          errors={errors('defaultBranch')}
          onChange={(event) => setDefaultBranch(event.target.value)}
        />
        <TextField
          label="User name"
          placeholder="For a token: empty"
          autoComplete="off"
          value={userName}
          errors={errors('userName')}
          onChange={(event) => setUserName(event.target.value)}
        />
        <TextField
          label="Token or password"
          type="password"
          autoComplete="new-password"
          placeholder="For a private repository"
          value={secret}
          errors={errors('secret')}
          onChange={(event) => setSecret(event.target.value)}
        />
      </div>
      <TextField
        label="Poll every (minutes)"
        inputMode="numeric"
        hint="0 not to poll, when a webhook brings the pushes."
        value={pollMinutes}
        errors={errors('pollMinutes')}
        onChange={(event) => setPollMinutes(event.target.value)}
      />
      {connect.isError && !(connect.error instanceof ApiError && connect.error.problem.field) && (
        <ErrorAlert error={connect.error} title="The repository was not connected" />
      )}
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button type="submit" variant="primary" busy={connect.isPending}>
          Connect
        </Button>
      </DialogActions>
    </form>
  );
}

/** Changes a repository's name, branch and polling, and for Generic Git its credential. */
export function ChangeRepositoryDialog({ repository, onClose }: { repository: RepositoryView; onClose: () => void }) {
  const update = useUpdateRepository(repository);
  const github = repository.kind === 'GitHub';
  const [name, setName] = useState(repository.name);
  const [defaultBranch, setDefaultBranch] = useState(repository.defaultBranch ?? '');
  const [pollMinutes, setPollMinutes] = useState(String(repository.pollMinutes));
  const [userName, setUserName] = useState(repository.userName ?? '');
  const [secret, setSecret] = useState('');
  const errors = (field: string) => (update.error instanceof ApiError ? update.error.fieldErrors(field) : []);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    update.mutate(
      {
        name: name.trim(),
        defaultBranch: defaultBranch.trim(),
        pollMinutes: Number(pollMinutes),
        ...(github ? {} : { userName: userName.trim(), ...(secret ? { secret } : {}) }),
      },
      { onSuccess: onClose },
    );
  };

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()} title={`Change ${repository.name}`} wide>
      <form onSubmit={submit} className="flex flex-col gap-3" noValidate>
        <div className="grid gap-3 sm:grid-cols-2">
          <TextField
            label="Name"
            value={name}
            errors={errors('name')}
            onChange={(event) => setName(event.target.value)}
          />
          <TextField
            label="Branch"
            placeholder="The repository's default"
            value={defaultBranch}
            errors={errors('defaultBranch')}
            onChange={(event) => setDefaultBranch(event.target.value)}
          />
          {!github && (
            <>
              <TextField
                label="User name"
                autoComplete="off"
                value={userName}
                errors={errors('userName')}
                onChange={(event) => setUserName(event.target.value)}
              />
              <TextField
                label="Token or password"
                type="password"
                autoComplete="new-password"
                placeholder={repository.hasCredential ? 'Unchanged' : 'None'}
                value={secret}
                errors={errors('secret')}
                onChange={(event) => setSecret(event.target.value)}
              />
            </>
          )}
        </div>
        <TextField
          label="Poll every (minutes)"
          inputMode="numeric"
          hint="0 not to poll."
          value={pollMinutes}
          errors={errors('pollMinutes')}
          onChange={(event) => setPollMinutes(event.target.value)}
        />
        {update.isError && !(update.error instanceof ApiError && update.error.problem.field) && (
          <ErrorAlert error={update.error} title="The repository was not changed" />
        )}
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="primary" busy={update.isPending}>
            Save
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}

/** Asks before disconnecting, saying what goes with it. */
export function DisconnectRepositoryDialog({
  repository,
  onClose,
}: {
  repository: RepositoryView;
  onClose: () => void;
}) {
  const remove = useDisconnectRepository(repository);
  return (
    <ConfirmDialog
      title={`Disconnect ${repository.name}?`}
      open
      onOpenChange={(open) => !open && onClose()}
      confirmLabel="Disconnect"
      busy={remove.isPending}
      error={remove.isError ? remove.error : null}
      onConfirm={() => remove.mutate(undefined, { onSuccess: onClose })}
    >
      {repository.automations === 1
        ? 'Its automation is deleted from this folder; its runs stay.'
        : `Its ${repository.automations} automations are deleted from this folder; their runs stay.`}{' '}
      Nothing in the repository changes.
    </ConfirmDialog>
  );
}

/** The new webhook address and its secret, shown once, with how to enter them at the forges. */
export function WebhookCreatedDialog({ created, onClose }: { created: CreatedRepositoryWebhook; onClose: () => void }) {
  const address = created.url ?? `${window.location.origin}${created.path}`;
  return (
    <OnceDialog
      title="Webhook address"
      description="Enter both at the forge now: they are shown only once. The old address, if any, stops working."
      saveLabel="I have entered them"
      onClose={onClose}
      wide
    >
      <CopyValue label="Address" value={address} />
      <CopyValue label="Secret" value={created.secret} />
      <ul className="list-disc space-y-1 pl-5 text-sm text-muted">
        <li>
          <strong className="text-ink">GitHub</strong>: Settings, Webhooks, Add webhook: payload URL, content type
          application/json, the secret, just the push event.
        </li>
        <li>
          <strong className="text-ink">Gitea, Forgejo</strong>: Settings, Webhooks, Add webhook, Gitea: target URL,
          POST, application/json, the secret, push events.
        </li>
        <li>
          <strong className="text-ink">GitLab</strong>: Settings, Webhooks: URL, the secret as secret token, push and
          tag push events.
        </li>
      </ul>
    </OnceDialog>
  );
}
