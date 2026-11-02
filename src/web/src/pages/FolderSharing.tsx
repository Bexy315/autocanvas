/**
 * Who may do what in a folder: the roles given here, to people and to groups, and whether the
 * folder is restricted (roles from the folders above stop applying). Owners change it; the server
 * shows the grants to owners only.
 */
import { Lock, Trash2, User, Users } from 'lucide-react';
import { useState } from 'react';
import type { FolderView } from '../api/folders';
import { useMe } from '../api/session';
import {
  useGrants,
  useRemoveGrant,
  useSetGrant,
  useSetRestriction,
  type FolderRole,
  type GrantView,
} from '../api/sharing';
import { ConfirmDialog } from '../dialogs/ConfirmDialog';
import { hasRole } from '../lib/roles';
import { ErrorAlert } from '../ui/Alert';
import { Button } from '../ui/Button';
import { Dialog, DialogActions } from '../ui/Dialog';
import { DirectoryPicker, type DirectoryEntry } from '../ui/DirectoryPicker';
import { Loading } from '../ui/Spinner';

const roles: { role: FolderRole; label: string; detail: string }[] = [
  { role: 'Viewer', label: 'Viewer', detail: 'reads automations, runs and logs' },
  { role: 'Operator', label: 'Operator', detail: 'also starts and cancels runs, sees variable values' },
  { role: 'Editor', label: 'Editor', detail: 'also changes automations, variables and secrets' },
  { role: 'Owner', label: 'Owner', detail: 'also shares and deletes the folder' },
];

function RoleSelect({
  value,
  label,
  onChange,
  disabled = false,
}: {
  value: FolderRole;
  label: string;
  onChange: (role: FolderRole) => void;
  disabled?: boolean;
}) {
  return (
    <select
      aria-label={label}
      value={value}
      disabled={disabled}
      onChange={(event) => onChange(event.target.value as FolderRole)}
      className="h-8 rounded-control border border-line-strong bg-surface px-2 text-sm disabled:opacity-60"
    >
      {roles.map((entry) => (
        <option key={entry.role} value={entry.role}>
          {entry.label}
        </option>
      ))}
    </select>
  );
}

export function FolderSharing({ folder, tag }: { folder: FolderView; tag: string | null }) {
  const me = useMe();
  const owner = hasRole(folder.myRole, 'Owner');
  const grants = useGrants(folder.id, owner);
  const set = useSetGrant(folder.id);
  const remove = useRemoveGrant(folder.id);
  const restrict = useSetRestriction(folder.id, tag);
  const [sharing, setSharing] = useState(false);
  const [removing, setRemoving] = useState<GrantView | null>(null);
  const [restricting, setRestricting] = useState(false);
  const myId = me.data?.user.id;

  if (!owner) {
    return (
      <p className="rounded-panel border border-line bg-surface px-4 py-3 text-sm text-muted">
        Only owners of the folder see who has access to it. Your role here is {folder.myRole}.
      </p>
    );
  }
  const items = grants.data ?? [];

  return (
    <div className="flex flex-col gap-4">
      <section
        aria-label="Restriction"
        className="flex flex-wrap items-start gap-3 rounded-panel border border-line bg-surface px-4 py-3"
      >
        <Lock aria-hidden="true" size={18} className={folder.isRestricted ? 'mt-0.5 text-warn' : 'mt-0.5 text-muted'} />
        <div className="flex min-w-0 flex-1 flex-col gap-1">
          <p className="font-medium">{folder.isRestricted ? 'Restricted' : 'Not restricted'}</p>
          <p className="text-sm text-muted">
            {folder.isRestricted
              ? 'Only the roles given here and below count; roles on the folders above do not reach in.'
              : 'Roles given on the folders above apply here too, in addition to those given here.'}
          </p>
        </div>
        <Button
          busy={restrict.isPending}
          onClick={() => (folder.isRestricted ? restrict.mutate(false) : setRestricting(true))}
        >
          {folder.isRestricted ? 'Lift the restriction' : 'Restrict'}
        </Button>
      </section>
      {restrict.isError && <ErrorAlert error={restrict.error} title="The restriction did not change" />}

      <section aria-label="Access" className="rounded-panel border border-line bg-surface">
        <div className="flex items-center justify-between gap-3 border-b border-line px-4 py-2.5">
          <h3 className="font-semibold">Who has access here</h3>
          <Button variant="primary" onClick={() => setSharing(true)}>
            Share
          </Button>
        </div>
        {grants.isPending && <Loading label="Loading access…" />}
        {grants.isError && <ErrorAlert error={grants.error} title="Access could not be loaded" />}
        {set.isError && <ErrorAlert error={set.error} title="The role did not change" />}
        {grants.data && items.length === 0 && (
          <p className="px-4 py-3 text-sm text-muted">Nobody was given a role here yet.</p>
        )}
        {items.length > 0 && (
          <ul aria-label="Roles given here" className="divide-y divide-line">
            {items.map((grant) => (
              <li key={grant.id} className="flex flex-wrap items-center gap-3 px-4 py-2">
                {grant.subjectType === 'user' ? (
                  <User aria-label="Person" size={16} className="text-muted" />
                ) : (
                  <Users aria-label="Group" size={16} className="text-muted" />
                )}
                <span className="min-w-0 flex-1 truncate font-medium">
                  {grant.subjectName}
                  {grant.subjectId === myId && <span className="font-normal text-muted"> (you)</span>}
                </span>
                <RoleSelect
                  value={grant.role}
                  label={`Role of ${grant.subjectName}`}
                  disabled={set.isPending}
                  onChange={(role) =>
                    set.mutate(
                      grant.subjectType === 'user'
                        ? { role, userId: grant.subjectId }
                        : { role, groupId: grant.subjectId },
                    )
                  }
                />
                <button
                  type="button"
                  aria-label={`Remove ${grant.subjectName}`}
                  title={`Remove ${grant.subjectName}`}
                  onClick={() => setRemoving(grant)}
                  className="rounded-control p-1.5 text-muted hover:bg-danger-soft hover:text-danger"
                >
                  <Trash2 aria-hidden="true" size={14} />
                </button>
              </li>
            ))}
          </ul>
        )}
        <dl className="grid gap-x-4 gap-y-1 border-t border-line px-4 py-3 text-xs sm:grid-cols-[auto_1fr]">
          {roles.map((entry) => (
            <div key={entry.role} className="contents">
              <dt className="font-medium">{entry.label}</dt>
              <dd className="text-muted">{entry.detail}</dd>
            </div>
          ))}
        </dl>
      </section>

      {sharing && <ShareDialog folder={folder} onClose={() => setSharing(false)} />}
      <ConfirmDialog
        title={`Remove ${removing?.subjectName ?? ''}?`}
        open={removing !== null}
        onOpenChange={(open) => !open && setRemoving(null)}
        confirmLabel="Remove access"
        busy={remove.isPending}
        error={remove.error}
        onConfirm={() => removing && remove.mutate(removing.id, { onSuccess: () => setRemoving(null) })}
      >
        {removing?.subjectId === myId && removing?.role === 'Owner'
          ? 'This is your own Owner role: without another way in, you lose access to the folder. An admin can give it back.'
          : 'Their role here ends; roles from folders above still apply unless the folder is restricted.'}
      </ConfirmDialog>
      <ConfirmDialog
        title={`Restrict ${folder.name}?`}
        open={restricting}
        onOpenChange={setRestricting}
        confirmLabel="Restrict"
        busy={restrict.isPending}
        error={restrict.error}
        onConfirm={() => restrict.mutate(true, { onSuccess: () => setRestricting(false) })}
      >
        Roles on the folders above stop applying here. Only those given on this folder and below count; you keep access
        as an owner.
      </ConfirmDialog>
    </div>
  );
}

function ShareDialog({ folder, onClose }: { folder: FolderView; onClose: () => void }) {
  const set = useSetGrant(folder.id);
  const [chosen, setChosen] = useState<DirectoryEntry | null>(null);
  const [role, setRole] = useState<FolderRole>('Viewer');
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()} title={`Share ${folder.name}`} wide>
      <div className="flex flex-col gap-4">
        {set.isError && <ErrorAlert error={set.error} title="It was not shared" />}
        {chosen ? (
          <p className="flex items-center gap-2 text-sm">
            {chosen.kind === 'user' ? <User aria-hidden="true" size={16} /> : <Users aria-hidden="true" size={16} />}
            <span className="font-medium">{chosen.name}</span>
            <span className="text-muted">{chosen.detail}</span>
            <button
              type="button"
              onClick={() => setChosen(null)}
              className="ml-auto text-xs text-accent hover:underline"
            >
              Choose someone else
            </button>
          </p>
        ) : (
          <DirectoryPicker label="Person or group" onPick={setChosen} />
        )}
        <fieldset className="flex flex-col gap-1.5">
          <legend className="mb-1 text-sm font-medium">Role</legend>
          {roles.map((entry) => (
            <label key={entry.role} className="flex items-baseline gap-2 text-sm">
              <input
                type="radio"
                name="role"
                checked={role === entry.role}
                onChange={() => setRole(entry.role)}
                className="accent-(--color-accent)"
              />
              <span className="font-medium">{entry.label}</span>
              <span className="text-muted">{entry.detail}</span>
            </label>
          ))}
        </fieldset>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button
            variant="primary"
            busy={set.isPending}
            disabled={chosen === null}
            onClick={() =>
              chosen &&
              set.mutate(chosen.kind === 'user' ? { role, userId: chosen.id } : { role, groupId: chosen.id }, {
                onSuccess: onClose,
              })
            }
          >
            Share
          </Button>
        </DialogActions>
      </div>
    </Dialog>
  );
}
