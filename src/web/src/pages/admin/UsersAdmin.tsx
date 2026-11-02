/**
 * The instance's users: created with a password the browser makes up and shows once, changed,
 * their password reset, their API tokens revoked, and deleted.
 */
import { KeyRound, Pencil, Plus, ShieldCheck, Trash2, UserPlus } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import {
  useCreateUser,
  useDeleteUser,
  useResetPassword,
  useRevokeUserToken,
  useUpdateUser,
  useUserTokens,
  useUsers,
  type UserView,
} from '../../api/admin';
import { ApiError } from '../../api/problem';
import { useMe } from '../../api/session';
import { ConfirmDialog } from '../../dialogs/ConfirmDialog';
import { generatePassword } from '../../lib/password';
import { ActionsMenu } from '../../ui/ActionsMenu';
import { ErrorAlert } from '../../ui/Alert';
import { Badge } from '../../ui/Badge';
import { Button } from '../../ui/Button';
import { Dialog, DialogActions } from '../../ui/Dialog';
import { EmptyState } from '../../ui/EmptyState';
import { RelativeTime } from '../../ui/RelativeTime';
import { CopyValue, OnceDialog } from '../../ui/SecretOnce';
import { Loading } from '../../ui/Spinner';
import { TextField } from '../../ui/TextField';

type Open = { kind: 'edit' | 'reset' | 'tokens' | 'delete'; user: UserView } | null;

const fieldErrors = (error: unknown, field: string) => (error instanceof ApiError ? error.fieldErrors(field) : []);

/** The message for a refusal that protects the last admin. */
function conflictText(error: unknown): string | null {
  return error instanceof ApiError && error.status === 409
    ? 'The last enabled admin cannot lose that role, be disabled or be deleted.'
    : null;
}

export function UsersAdmin() {
  const users = useUsers();
  const me = useMe();
  const [creating, setCreating] = useState(false);
  const [open, setOpen] = useState<Open>(null);
  const [password, setPassword] = useState<{ userName: string; password: string } | null>(null);
  const items = users.data?.pages.flatMap((page) => page.items) ?? [];

  return (
    <div className="flex flex-col gap-3">
      <div className="flex justify-end">
        <Button variant="primary" onClick={() => setCreating(true)}>
          <UserPlus aria-hidden="true" size={14} />
          New user
        </Button>
      </div>
      {users.isPending && <Loading label="Loading users…" />}
      {users.isError && <ErrorAlert error={users.error} title="The users could not be loaded" />}
      {users.data && items.length === 0 && (
        <EmptyState Icon={UserPlus} title="No users">
          Add the people who use this instance.
        </EmptyState>
      )}
      {items.length > 0 && (
        <ul aria-label="Users" className="divide-y divide-line rounded-panel border border-line bg-surface">
          {items.map((user) => (
            <li key={user.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 px-4 py-2.5">
              <span className="font-medium">{user.userName}</span>
              <span className="min-w-0 flex-1 truncate text-sm text-muted">
                {user.displayName}
                {user.email && ` · ${user.email}`}
              </span>
              {user.isAdmin && <Badge tone="accent">Admin</Badge>}
              {user.isDisabled && <Badge tone="warn">Disabled</Badge>}
              {user.mustChangePassword && <Badge>Must change password</Badge>}
              <span className="text-xs text-muted">
                {user.lastSignInAt ? (
                  <>
                    signed in <RelativeTime value={user.lastSignInAt} />
                  </>
                ) : (
                  'never signed in'
                )}
              </span>
              <ActionsMenu
                label={`Actions for ${user.userName}`}
                actions={[
                  { label: 'Change', Icon: Pencil, onSelect: () => setOpen({ kind: 'edit', user }) },
                  { label: 'Reset password', Icon: KeyRound, onSelect: () => setOpen({ kind: 'reset', user }) },
                  { label: 'API tokens', Icon: ShieldCheck, onSelect: () => setOpen({ kind: 'tokens', user }) },
                  ...(user.id === me.data?.user.id
                    ? []
                    : [
                        {
                          label: 'Delete',
                          Icon: Trash2,
                          onSelect: () => setOpen({ kind: 'delete', user }),
                          danger: true,
                        },
                      ]),
                ]}
              />
            </li>
          ))}
        </ul>
      )}
      {users.hasNextPage && (
        <Button className="self-center" busy={users.isFetchingNextPage} onClick={() => void users.fetchNextPage()}>
          Load more users
        </Button>
      )}

      {creating && (
        <CreateUserDialog
          onClose={() => setCreating(false)}
          onCreated={(created) => {
            setCreating(false);
            setPassword(created);
          }}
        />
      )}
      {open?.kind === 'edit' && <EditUserDialog user={open.user} onClose={() => setOpen(null)} />}
      {open?.kind === 'reset' && (
        <ResetPasswordDialog
          user={open.user}
          onClose={() => setOpen(null)}
          onReset={(reset) => {
            setOpen(null);
            setPassword(reset);
          }}
        />
      )}
      {open?.kind === 'tokens' && <UserTokensDialog user={open.user} onClose={() => setOpen(null)} />}
      {open?.kind === 'delete' && <DeleteUserDialog user={open.user} onClose={() => setOpen(null)} />}
      {password && (
        <OnceDialog
          title={`Password of ${password.userName}`}
          description="Give it to them; they choose their own at the first sign-in."
          saveLabel="I have passed the password on"
          onClose={() => setPassword(null)}
        >
          <CopyValue label="User name" value={password.userName} />
          <CopyValue label="Password" value={password.password} />
        </OnceDialog>
      )}
    </div>
  );
}

function CreateUserDialog({
  onClose,
  onCreated,
}: {
  onClose: () => void;
  onCreated: (created: { userName: string; password: string }) => void;
}) {
  const create = useCreateUser();
  const [userName, setUserName] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [email, setEmail] = useState('');
  const [isAdmin, setIsAdmin] = useState(false);
  const submit = (event: FormEvent) => {
    event.preventDefault();
    const password = generatePassword();
    create.mutate(
      {
        userName: userName.trim(),
        password,
        displayName: displayName.trim() === '' ? null : displayName.trim(),
        email: email.trim() === '' ? null : email.trim(),
        isAdmin,
      },
      { onSuccess: (user) => onCreated({ userName: user.userName, password }) },
    );
  };
  const other = create.isError && fieldErrors(create.error, 'userName').length === 0;
  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onClose()}
      title="New user"
      description="AutoCanvas makes up a first password, shown once; they replace it at the first sign-in."
    >
      <form onSubmit={submit} className="flex flex-col gap-4" noValidate>
        {other && <ErrorAlert error={create.error} />}
        <TextField
          label="User name"
          required
          value={userName}
          hint="Letters, digits, dot, underscore, hyphen; used to sign in."
          errors={fieldErrors(create.error, 'userName')}
          onChange={(event) => setUserName(event.target.value)}
        />
        <TextField
          label="Display name"
          value={displayName}
          hint="Shown to others; the user name if empty."
          onChange={(event) => setDisplayName(event.target.value)}
        />
        <TextField
          label="Email"
          type="email"
          value={email}
          hint="Information only."
          onChange={(event) => setEmail(event.target.value)}
        />
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={isAdmin}
            onChange={(event) => setIsAdmin(event.target.checked)}
            className="size-4 accent-(--color-accent)"
          />
          Admin of this instance
        </label>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="primary" busy={create.isPending} disabled={userName.trim() === ''}>
            <Plus aria-hidden="true" size={14} />
            Create user
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}

function EditUserDialog({ user, onClose }: { user: UserView; onClose: () => void }) {
  const update = useUpdateUser(user.id);
  const [displayName, setDisplayName] = useState(user.displayName);
  const [email, setEmail] = useState(user.email ?? '');
  const [isAdmin, setIsAdmin] = useState(user.isAdmin);
  const [isDisabled, setIsDisabled] = useState(user.isDisabled);
  const submit = (event: FormEvent) => {
    event.preventDefault();
    update.mutate(
      { displayName: displayName.trim(), email: email.trim() === '' ? null : email.trim(), isAdmin, isDisabled },
      { onSuccess: onClose },
    );
  };
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()} title={`Change ${user.userName}`}>
      <form onSubmit={submit} className="flex flex-col gap-4" noValidate>
        {update.isError && (
          <ErrorAlert error={update.error} title={conflictText(update.error) ?? 'It was not changed'} />
        )}
        <TextField
          label="Display name"
          value={displayName}
          errors={fieldErrors(update.error, 'displayName')}
          onChange={(event) => setDisplayName(event.target.value)}
        />
        <TextField
          label="Email"
          type="email"
          value={email}
          errors={fieldErrors(update.error, 'email')}
          onChange={(event) => setEmail(event.target.value)}
        />
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={isAdmin}
            onChange={(event) => setIsAdmin(event.target.checked)}
            className="size-4 accent-(--color-accent)"
          />
          Admin of this instance
        </label>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={isDisabled}
            onChange={(event) => setIsDisabled(event.target.checked)}
            className="size-4 accent-(--color-accent)"
          />
          Disabled: cannot sign in, and their sessions and tokens stop working
        </label>
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

function ResetPasswordDialog({
  user,
  onClose,
  onReset,
}: {
  user: UserView;
  onClose: () => void;
  onReset: (reset: { userName: string; password: string }) => void;
}) {
  const reset = useResetPassword(user.id);
  return (
    <ConfirmDialog
      title={`Reset the password of ${user.userName}?`}
      open
      onOpenChange={(open) => !open && onClose()}
      confirmLabel="Reset password"
      busy={reset.isPending}
      error={reset.isError ? reset.error : null}
      onConfirm={() => {
        const password = generatePassword();
        reset.mutate(password, { onSuccess: () => onReset({ userName: user.userName, password }) });
      }}
    >
      AutoCanvas makes up a new password, shown once. Their sessions end, and they choose their own at the next sign-in.
    </ConfirmDialog>
  );
}

function UserTokensDialog({ user, onClose }: { user: UserView; onClose: () => void }) {
  const tokens = useUserTokens(user.id, true);
  const revoke = useRevokeUserToken(user.id);
  const items = tokens.data ?? [];
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()} title={`API tokens of ${user.userName}`} wide>
      <div className="flex flex-col gap-3">
        {tokens.isPending && <Loading label="Loading tokens…" />}
        {tokens.isError && <ErrorAlert error={tokens.error} />}
        {revoke.isError && <ErrorAlert error={revoke.error} title="The token was not revoked" />}
        {tokens.data && items.length === 0 && <p className="text-sm text-muted">They have no API tokens.</p>}
        {items.length > 0 && (
          <ul aria-label="Tokens" className="divide-y divide-line rounded-control border border-line">
            {items.map((token) => (
              <li key={token.id} className="flex flex-wrap items-center gap-3 px-3 py-2 text-sm">
                <span className="font-medium">{token.name}</span>
                <code className="font-mono text-xs text-muted">{token.hint}</code>
                {token.isExpired && <Badge tone="warn">Expired</Badge>}
                <span className="ml-auto text-xs text-muted">
                  {token.lastUsedAt ? (
                    <>
                      used <RelativeTime value={token.lastUsedAt} />
                    </>
                  ) : (
                    'never used'
                  )}
                </span>
                <Button
                  variant="danger"
                  busy={revoke.isPending && revoke.variables === token.id}
                  onClick={() => revoke.mutate(token.id)}
                >
                  Revoke
                </Button>
              </li>
            ))}
          </ul>
        )}
        <DialogActions>
          <Button onClick={onClose}>Close</Button>
        </DialogActions>
      </div>
    </Dialog>
  );
}

function DeleteUserDialog({ user, onClose }: { user: UserView; onClose: () => void }) {
  const remove = useDeleteUser(user.id);
  return (
    <ConfirmDialog
      title={`Delete ${user.userName}?`}
      open
      onOpenChange={(open) => !open && onClose()}
      confirmLabel="Delete user"
      busy={remove.isPending}
      error={
        remove.isError
          ? conflictText(remove.error)
            ? new Error(conflictText(remove.error) ?? '')
            : remove.error
          : null
      }
      onConfirm={() => remove.mutate(undefined, { onSuccess: onClose })}
    >
      The account goes with its sessions, tokens and memberships; the audit log keeps the name. To keep it but stop its
      use, disable it instead.
    </ConfirmDialog>
  );
}
