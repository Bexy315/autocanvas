import { useState } from 'react';
import { useMe } from '../api/session';
import { Plus, Trash2 } from 'lucide-react';
import { useCreateMyToken, useDeleteMyToken, useEndSession, useMySessions, useMyTokens } from '../api/sharing';
import { ConfirmDialog } from '../dialogs/ConfirmDialog';
import { Alert, ErrorAlert } from '../ui/Alert';
import { Badge } from '../ui/Badge';
import { Button } from '../ui/Button';
import { Dialog, DialogActions } from '../ui/Dialog';
import { RelativeTime } from '../ui/RelativeTime';
import { CopyValue, OnceDialog } from '../ui/SecretOnce';
import { Loading } from '../ui/Spinner';
import { TextField } from '../ui/TextField';
import { PasswordForm } from './PasswordForm';

function formatDate(value: string | null | undefined): string {
  return value ? new Date(value).toLocaleString() : 'Never';
}

/** The signed-in user's own account: profile, password, where they are signed in, and API tokens. */
export function AccountPage() {
  const me = useMe();
  const [changed, setChanged] = useState(false);
  const user = me.data?.user;
  if (!user) {
    return null;
  }

  const groups = me.data?.groups ?? [];
  const facts: [string, string][] = [
    ['User name', user.userName],
    ['Display name', user.displayName],
    ['Email', user.email ?? 'Not given'],
    ['Role', user.isAdmin ? 'Administrator' : 'User'],
    ['Groups', groups.length > 0 ? groups.map((group) => group.name).join(', ') : 'None'],
    ['Last sign-in', formatDate(user.lastSignInAt)],
  ];

  return (
    <div className="mx-auto flex max-w-3xl flex-col gap-6 p-6">
      <h1 className="text-xl font-semibold">Account</h1>
      <section aria-labelledby="profile" className="rounded-panel border border-line bg-surface">
        <h2 id="profile" className="border-b border-line px-4 py-2.5 font-semibold">
          Profile
        </h2>
        <dl className="grid grid-cols-[10rem_1fr] gap-x-4 gap-y-2 px-4 py-3">
          {facts.map(([term, value]) => (
            <div key={term} className="contents">
              <dt className="text-muted">{term}</dt>
              <dd>{value}</dd>
            </div>
          ))}
        </dl>
      </section>
      <section aria-labelledby="password" className="rounded-panel border border-line bg-surface">
        <h2 id="password" className="border-b border-line px-4 py-2.5 font-semibold">
          Password
        </h2>
        <div className="flex max-w-sm flex-col gap-4 px-4 py-4">
          {changed && (
            <Alert tone="ok" title="Password changed">
              Your other sessions have ended; this one stays signed in.
            </Alert>
          )}
          <PasswordForm submitLabel="Change password" onChanged={() => setChanged(true)} />
        </div>
      </section>
      <Sessions />
      <Tokens />
    </div>
  );
}

/** Where the user is signed in; any other session can be ended here. */
function Sessions() {
  const sessions = useMySessions();
  const end = useEndSession();
  const items = sessions.data ?? [];
  return (
    <section aria-labelledby="sessions" className="rounded-panel border border-line bg-surface">
      <h2 id="sessions" className="border-b border-line px-4 py-2.5 font-semibold">
        Where you are signed in
      </h2>
      {sessions.isPending && <Loading label="Loading sessions…" />}
      {sessions.isError && <ErrorAlert error={sessions.error} />}
      {end.isError && <ErrorAlert error={end.error} title="The session did not end" />}
      <ul aria-label="Sessions" className="divide-y divide-line">
        {items.map((session) => (
          <li key={session.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 px-4 py-2.5 text-sm">
            <span className="min-w-0 flex-1 truncate" title={session.userAgent ?? undefined}>
              {session.userAgent ?? 'Unknown browser'}
            </span>
            {session.isCurrent && <Badge tone="accent">This browser</Badge>}
            <span className="text-xs text-muted">
              {session.clientAddress ?? 'unknown address'} · since <RelativeTime value={session.createdAt} /> · last{' '}
              <RelativeTime value={session.lastSeenAt} />
            </span>
            {!session.isCurrent && (
              <Button busy={end.isPending && end.variables === session.id} onClick={() => end.mutate(session.id)}>
                Sign out there
              </Button>
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}

const tokenExpiries = [
  { days: 30, label: 'in 30 days' },
  { days: 90, label: 'in 90 days' },
  { days: 365, label: 'in a year' },
  { days: 0, label: 'never' },
];

/** API tokens for scripts and other machines; the secret is shown once. */
function Tokens() {
  const tokens = useMyTokens();
  const remove = useDeleteMyToken();
  const [creating, setCreating] = useState(false);
  const [created, setCreated] = useState<string | null>(null);
  const [deleting, setDeleting] = useState<{ id: string; name: string } | null>(null);
  const items = tokens.data ?? [];
  return (
    <section aria-labelledby="tokens" className="rounded-panel border border-line bg-surface">
      <div className="flex items-center justify-between gap-3 border-b border-line px-4 py-2.5">
        <h2 id="tokens" className="font-semibold">
          API tokens
        </h2>
        <Button onClick={() => setCreating(true)}>
          <Plus aria-hidden="true" size={14} />
          New token
        </Button>
      </div>
      <p className="px-4 pt-2 text-xs text-muted">For scripts and other systems: they act as you, with your roles.</p>
      {tokens.isPending && <Loading label="Loading tokens…" />}
      {tokens.isError && <ErrorAlert error={tokens.error} />}
      {tokens.data && items.length === 0 && <p className="px-4 py-3 text-sm text-muted">You have no API tokens.</p>}
      {items.length > 0 && (
        <ul aria-label="API tokens" className="divide-y divide-line">
          {items.map((token) => (
            <li key={token.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 px-4 py-2.5 text-sm">
              <span className="font-medium">{token.name}</span>
              <code className="font-mono text-xs text-muted">{token.hint}</code>
              {token.isExpired && <Badge tone="warn">Expired</Badge>}
              <span className="ml-auto text-xs text-muted">
                {token.expiresAt ? (
                  <>
                    expires <RelativeTime value={token.expiresAt} />
                  </>
                ) : (
                  'does not expire'
                )}{' '}
                ·{' '}
                {token.lastUsedAt ? (
                  <>
                    used <RelativeTime value={token.lastUsedAt} />
                  </>
                ) : (
                  'never used'
                )}
              </span>
              <button
                type="button"
                aria-label={`Delete the token ${token.name}`}
                title="Delete"
                onClick={() => setDeleting({ id: token.id, name: token.name })}
                className="rounded-control p-1.5 text-muted hover:bg-danger-soft hover:text-danger"
              >
                <Trash2 aria-hidden="true" size={14} />
              </button>
            </li>
          ))}
        </ul>
      )}
      {creating && (
        <NewTokenDialog
          onClose={() => setCreating(false)}
          onCreated={(token) => {
            setCreating(false);
            setCreated(token);
          }}
        />
      )}
      {created && (
        <OnceDialog title="API token created" saveLabel="I have saved the token" onClose={() => setCreated(null)} wide>
          <CopyValue label="Token" value={created} />
          <CopyValue
            label="Example"
            value={`curl -H "Authorization: Bearer ${created}" ${globalThis.location.origin}/api/v1/me`}
          />
        </OnceDialog>
      )}
      <ConfirmDialog
        title={`Delete the token ${deleting?.name ?? ''}?`}
        open={deleting !== null}
        onOpenChange={(open) => !open && setDeleting(null)}
        confirmLabel="Delete token"
        busy={remove.isPending}
        error={remove.isError ? remove.error : null}
        onConfirm={() => deleting && remove.mutate(deleting.id, { onSuccess: () => setDeleting(null) })}
      >
        Requests with it fail from now on.
      </ConfirmDialog>
    </section>
  );
}

function NewTokenDialog({ onClose, onCreated }: { onClose: () => void; onCreated: (token: string) => void }) {
  const create = useCreateMyToken();
  const [name, setName] = useState('');
  const [days, setDays] = useState(90);
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()} title="New API token">
      <form
        onSubmit={(event) => {
          event.preventDefault();
          create.mutate(
            {
              name: name.trim(),
              expiresAt: days === 0 ? null : new Date(Date.now() + days * 24 * 3600 * 1000).toISOString(),
            },
            { onSuccess: (result) => onCreated(result.token) },
          );
        }}
        className="flex flex-col gap-4"
        noValidate
      >
        {create.isError && <ErrorAlert error={create.error} />}
        <TextField
          label="What it is for"
          required
          value={name}
          placeholder="deploy script"
          onChange={(event) => setName(event.target.value)}
        />
        <label className="flex flex-col gap-1 text-sm">
          <span className="font-medium">Expires</span>
          <select
            value={days}
            onChange={(event) => setDays(Number(event.target.value))}
            className="h-9 rounded-control border border-line-strong bg-surface px-2"
          >
            {tokenExpiries.map((entry) => (
              <option key={entry.days} value={entry.days}>
                {entry.label}
              </option>
            ))}
          </select>
        </label>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="primary" busy={create.isPending} disabled={name.trim() === ''}>
            Create token
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}
