import { ExternalLink, GitBranch, KeyRound, Trash2 } from 'lucide-react';
import { useId, useState, type ChangeEvent, type FormEvent } from 'react';
import {
  useConfigureGitHubApp,
  useDeleteGitHubApp,
  useGitHubApp,
  useGitHubManifest,
  type GitHubApp,
} from '../../api/forges';
import { ApiError } from '../../api/problem';
import { ConfirmDialog } from '../../dialogs/ConfirmDialog';
import { postForm } from '../../lib/postForm';
import { Alert, ErrorAlert } from '../../ui/Alert';
import { Badge } from '../../ui/Badge';
import { Button } from '../../ui/Button';
import { Dialog, DialogActions } from '../../ui/Dialog';
import { EmptyState } from '../../ui/EmptyState';
import { RelativeTime } from '../../ui/RelativeTime';
import { CopyValue } from '../../ui/SecretOnce';
import { Loading } from '../../ui/Spinner';
import { TextField } from '../../ui/TextField';

/** The forges repositories live on (ADR-0022): for now the instance's GitHub App. */
export function ForgesAdmin() {
  const github = useGitHubApp();
  if (github.isPending) {
    return <Loading label="Loading the GitHub App…" />;
  }
  if (github.isError) {
    return <ErrorAlert error={github.error} title="The GitHub App could not be loaded" />;
  }
  return github.data.app ? <GitHubAppPanel app={github.data.app} /> : <NoGitHubApp />;
}

function NoGitHubApp() {
  const manifest = useGitHubManifest();
  const [organization, setOrganization] = useState('');
  const [webAddress, setWebAddress] = useState('');
  const [entering, setEntering] = useState(false);
  const fieldErrors = (field: string) => (manifest.error instanceof ApiError ? manifest.error.fieldErrors(field) : []);

  const create = (event: FormEvent) => {
    event.preventDefault();
    manifest.mutate(
      { organization: organization.trim() || null, webAddress: webAddress.trim() || null },
      {
        // The server's page takes the browser on to GitHub: this page may post forms to itself only.
        onSuccess: (created) => postForm('/api/v1/forges/github/manifest/launch', { state: created.state }),
      },
    );
  };

  return (
    <div className="flex flex-col gap-4">
      <EmptyState Icon={GitBranch} title="No GitHub App yet">
        Connect GitHub repositories through a GitHub App of this instance: AutoCanvas reads them with short-lived
        tokens, and GitHub sends their pushes to it. One app serves every folder.
      </EmptyState>
      <section
        aria-labelledby="create-app"
        className="flex flex-col gap-3 rounded-panel border border-line bg-surface p-4"
      >
        <h2 id="create-app" className="text-base font-semibold">
          Create the app on GitHub
        </h2>
        <p className="text-sm text-muted">
          AutoCanvas sends GitHub a description of the app; you look it over there, may rename it, and create it. GitHub
          then sends you back here. GitHub needs to reach this instance at its public address (Instance tab) for the way
          back and for events.
        </p>
        <form onSubmit={create} className="flex max-w-xl flex-col gap-3" noValidate>
          <TextField
            label="Organization"
            hint="Create the app in this GitHub organization; leave it empty for your own account."
            placeholder="Your own account"
            value={organization}
            errors={fieldErrors('organization')}
            onChange={(event) => setOrganization(event.target.value)}
          />
          <details className="text-sm">
            <summary className="cursor-pointer text-muted">GitHub Enterprise Server</summary>
            <TextField
              className="mt-2"
              label="GitHub address"
              hint="The web address of your GitHub Enterprise Server; empty for github.com."
              placeholder="https://github.com"
              value={webAddress}
              errors={fieldErrors('webAddress')}
              onChange={(event) => setWebAddress(event.target.value)}
            />
          </details>
          {manifest.isError && !(manifest.error instanceof ApiError && manifest.error.problem.field) && (
            <ErrorAlert error={manifest.error} title="GitHub cannot be asked yet" />
          )}
          <div className="flex flex-wrap gap-2">
            <Button type="submit" variant="primary" busy={manifest.isPending || manifest.isSuccess}>
              <GitBranch aria-hidden="true" size={14} />
              Create GitHub App
            </Button>
            <Button onClick={() => setEntering(true)}>
              <KeyRound aria-hidden="true" size={14} />
              Enter an existing app
            </Button>
          </div>
        </form>
      </section>
      {entering && <GitHubAppDialog onClose={() => setEntering(false)} />}
    </div>
  );
}

function GitHubAppPanel({ app }: { app: GitHubApp }) {
  const [open, setOpen] = useState<'key' | 'remove' | null>(null);
  return (
    <div className="flex flex-col gap-4">
      <section
        aria-labelledby="github-app"
        className="flex flex-col gap-3 rounded-panel border border-line bg-surface p-4"
      >
        <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
          <GitBranch aria-hidden="true" size={18} />
          <h2 id="github-app" className="text-base font-semibold">
            <a href={app.htmlUrl} target="_blank" rel="noreferrer" className="hover:underline">
              {app.name}
            </a>
          </h2>
          <Badge tone="ok">Set up</Badge>
          <span className="ml-auto text-xs text-muted">
            by {app.createdByName} <RelativeTime value={app.createdAt} />
          </span>
        </div>
        <dl className="grid grid-cols-[max-content_1fr] gap-x-4 gap-y-1 text-sm">
          <dt className="text-muted">Owner</dt>
          <dd>{app.ownerLogin}</dd>
          <dt className="text-muted">App ID</dt>
          <dd className="font-mono">{app.appId}</dd>
          <dt className="text-muted">GitHub</dt>
          <dd>{app.webAddress.replace(/\/$/, '')}</dd>
        </dl>
        {app.webhookUrl ? (
          <CopyValue label="Events address" value={app.webhookUrl} />
        ) : (
          <Alert tone="warn" title="GitHub cannot send events yet">
            Set the public address on the Instance tab; until then, connected repositories are polled.
          </Alert>
        )}
        <div className="flex flex-wrap gap-2">
          <Button onClick={() => setOpen('key')}>
            <KeyRound aria-hidden="true" size={14} />
            Enter a new key or secret
          </Button>
          <Button variant="danger" onClick={() => setOpen('remove')}>
            <Trash2 aria-hidden="true" size={14} />
            Remove
          </Button>
        </div>
      </section>
      <Installations app={app} />
      {open === 'key' && <GitHubAppDialog app={app} onClose={() => setOpen(null)} />}
      {open === 'remove' && <RemoveDialog app={app} onClose={() => setOpen(null)} />}
    </div>
  );
}

function Installations({ app }: { app: GitHubApp }) {
  return (
    <section aria-labelledby="installations" className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        <h2 id="installations" className="text-base font-semibold">
          Installations
        </h2>
        <a
          href={app.installUrl}
          target="_blank"
          rel="noreferrer"
          className="ml-auto inline-flex h-8 items-center gap-1.5 rounded-control border border-line-strong bg-surface px-3 text-sm hover:bg-sunken"
        >
          <ExternalLink aria-hidden="true" size={14} />
          Install on an account
        </a>
      </div>
      <p className="text-sm text-muted">
        The app reaches the repositories of the accounts and organizations it is installed on; choose which ones there.
        Editors of a folder then connect them in the folder.
      </p>
      {app.installationsError && (
        <ErrorAlert error={new Error(app.installationsError)} title="GitHub could not be asked for the installations" />
      )}
      {!app.installationsError && app.installations.length === 0 && (
        <Alert tone="info">Not installed anywhere yet.</Alert>
      )}
      {app.installations.length > 0 && (
        <ul aria-label="Installations" className="divide-y divide-line rounded-panel border border-line bg-surface">
          {app.installations.map((installation) => (
            <li key={installation.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 px-4 py-2.5">
              <span className="font-medium">{installation.account}</span>
              <span className="text-xs text-muted">{installation.accountType}</span>
              <Badge>{installation.allRepositories ? 'All repositories' : 'Chosen repositories'}</Badge>
              {installation.suspended && <Badge tone="warn">Suspended</Badge>}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

/** Enters an app made on GitHub by hand, or a new key or secret of the app there is. */
function GitHubAppDialog({ app, onClose }: { app?: GitHubApp; onClose: () => void }) {
  const configure = useConfigureGitHubApp();
  const keyId = useId();
  const [appId, setAppId] = useState(app ? String(app.appId) : '');
  const [privateKey, setPrivateKey] = useState('');
  const [webhookSecret, setWebhookSecret] = useState('');
  const [webAddress, setWebAddress] = useState(app?.webAddress.replace(/\/$/, '') ?? '');
  const fieldErrors = (field: string) =>
    configure.error instanceof ApiError ? configure.error.fieldErrors(field) : [];

  const load = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    if (file) {
      setPrivateKey(await file.text());
    }
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    configure.mutate(
      { appId: Number(appId), privateKey, webhookSecret, webAddress: webAddress.trim() || null },
      { onSuccess: onClose },
    );
  };

  const keyErrors = fieldErrors('privateKey');
  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onClose()}
      title={app ? 'Enter a new key or secret' : 'Enter an existing GitHub App'}
      description="From the app's settings on GitHub. AutoCanvas checks them with GitHub and keeps them encrypted."
      wide
    >
      <form onSubmit={submit} className="flex flex-col gap-3" noValidate>
        <TextField
          label="App ID"
          inputMode="numeric"
          value={appId}
          readOnly={Boolean(app)}
          errors={fieldErrors('appId')}
          onChange={(event) => setAppId(event.target.value)}
        />
        <div className="flex flex-col gap-1">
          <label htmlFor={keyId} className="text-sm font-medium text-ink">
            Private key
          </label>
          <textarea
            id={keyId}
            rows={6}
            spellCheck={false}
            placeholder="-----BEGIN RSA PRIVATE KEY-----"
            value={privateKey}
            aria-invalid={keyErrors.length > 0 || undefined}
            onChange={(event) => setPrivateKey(event.target.value)}
            className="resize-y rounded-control border border-line-strong bg-surface px-2.5 py-1.5 font-mono text-xs text-ink"
          />
          <label className="text-xs text-muted">
            Or load the .pem file GitHub gave you:{' '}
            <input type="file" accept=".pem" className="text-xs" onChange={(event) => void load(event)} />
          </label>
          {keyErrors.length > 0 && <p className="text-xs text-danger">{keyErrors.join(' ')}</p>}
        </div>
        <TextField
          label="Webhook secret"
          type="password"
          autoComplete="off"
          value={webhookSecret}
          errors={fieldErrors('webhookSecret')}
          onChange={(event) => setWebhookSecret(event.target.value)}
        />
        <TextField
          label="GitHub address"
          hint="Only for a GitHub Enterprise Server; empty for github.com."
          placeholder="https://github.com"
          value={webAddress}
          errors={fieldErrors('webAddress')}
          onChange={(event) => setWebAddress(event.target.value)}
        />
        {configure.isError && !(configure.error instanceof ApiError && configure.error.problem.field) && (
          <ErrorAlert error={configure.error} title="The app was not entered" />
        )}
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="primary" busy={configure.isPending}>
            {app ? 'Save' : 'Enter app'}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}

function RemoveDialog({ app, onClose }: { app: GitHubApp; onClose: () => void }) {
  const remove = useDeleteGitHubApp();
  return (
    <ConfirmDialog
      title={`Remove ${app.name}?`}
      open
      onOpenChange={(open) => !open && onClose()}
      confirmLabel="Remove app"
      busy={remove.isPending}
      error={remove.isError ? remove.error : null}
      onConfirm={() => remove.mutate(undefined, { onSuccess: onClose })}
    >
      AutoCanvas forgets the app, its key and secrets; on GitHub it stays until you delete it there. This works only
      once no GitHub repository is connected.
    </ConfirmDialog>
  );
}
