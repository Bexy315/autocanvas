/** Groups of users, to share folders with several people at once; for admins. */
import { Pencil, Plus, Trash2, Users, X } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import {
  useCreateGroup,
  useDeleteGroup,
  useGroup,
  useGroupMember,
  useGroups,
  useUpdateGroup,
  type GroupView,
} from '../../api/admin';
import { ApiError } from '../../api/problem';
import { ConfirmDialog } from '../../dialogs/ConfirmDialog';
import { ErrorAlert } from '../../ui/Alert';
import { Button } from '../../ui/Button';
import { Dialog, DialogActions } from '../../ui/Dialog';
import { DirectoryPicker } from '../../ui/DirectoryPicker';
import { EmptyState } from '../../ui/EmptyState';
import { Loading } from '../../ui/Spinner';
import { TextField } from '../../ui/TextField';

const fieldErrors = (error: unknown, field: string) => (error instanceof ApiError ? error.fieldErrors(field) : []);

export function GroupsAdmin() {
  const groups = useGroups();
  const [editing, setEditing] = useState<GroupView | 'new' | null>(null);
  const [members, setMembers] = useState<GroupView | null>(null);
  const [deleting, setDeleting] = useState<GroupView | null>(null);
  const items = groups.data?.pages.flatMap((page) => page.items) ?? [];

  return (
    <div className="flex flex-col gap-3">
      <div className="flex justify-end">
        <Button variant="primary" onClick={() => setEditing('new')}>
          <Plus aria-hidden="true" size={14} />
          New group
        </Button>
      </div>
      {groups.isPending && <Loading label="Loading groups…" />}
      {groups.isError && <ErrorAlert error={groups.error} title="The groups could not be loaded" />}
      {groups.data && items.length === 0 && (
        <EmptyState Icon={Users} title="No groups">
          Groups share folders with several people at once.
        </EmptyState>
      )}
      {items.length > 0 && (
        <ul aria-label="Groups" className="divide-y divide-line rounded-panel border border-line bg-surface">
          {items.map((group) => (
            <li key={group.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 px-4 py-2.5">
              <button
                type="button"
                onClick={() => setMembers(group)}
                className="font-medium text-accent hover:underline"
              >
                {group.name}
              </button>
              <span className="min-w-0 flex-1 truncate text-sm text-muted">{group.description}</span>
              <span className="text-xs text-muted">
                {Number(group.memberCount) === 1 ? '1 member' : `${Number(group.memberCount)} members`}
              </span>
              <button
                type="button"
                aria-label={`Change ${group.name}`}
                title="Change"
                onClick={() => setEditing(group)}
                className="rounded-control p-1.5 text-muted hover:bg-sunken hover:text-ink"
              >
                <Pencil aria-hidden="true" size={14} />
              </button>
              <button
                type="button"
                aria-label={`Delete ${group.name}`}
                title="Delete"
                onClick={() => setDeleting(group)}
                className="rounded-control p-1.5 text-muted hover:bg-danger-soft hover:text-danger"
              >
                <Trash2 aria-hidden="true" size={14} />
              </button>
            </li>
          ))}
        </ul>
      )}
      {groups.hasNextPage && (
        <Button className="self-center" busy={groups.isFetchingNextPage} onClick={() => void groups.fetchNextPage()}>
          Load more groups
        </Button>
      )}
      {editing && <GroupDialog group={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
      {members && <MembersDialog group={members} onClose={() => setMembers(null)} />}
      {deleting && <DeleteGroupDialog group={deleting} onClose={() => setDeleting(null)} />}
    </div>
  );
}

function GroupDialog({ group, onClose }: { group: GroupView | null; onClose: () => void }) {
  const create = useCreateGroup();
  const update = useUpdateGroup(group?.id ?? '');
  const save = group ? update : create;
  const [name, setName] = useState(group?.name ?? '');
  const [description, setDescription] = useState(group?.description ?? '');
  const submit = (event: FormEvent) => {
    event.preventDefault();
    const body = { name: name.trim(), description: description.trim() === '' ? null : description.trim() };
    if (group) {
      update.mutate(body, { onSuccess: onClose });
    } else {
      create.mutate(body, { onSuccess: onClose });
    }
  };
  const other = save.isError && fieldErrors(save.error, 'name').length === 0;
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()} title={group ? `Change ${group.name}` : 'New group'}>
      <form onSubmit={submit} className="flex flex-col gap-4" noValidate>
        {other && <ErrorAlert error={save.error} />}
        <TextField
          label="Name"
          required
          value={name}
          errors={fieldErrors(save.error, 'name')}
          onChange={(event) => setName(event.target.value)}
        />
        <TextField
          label="Description"
          value={description}
          hint="Optional: who belongs in it."
          onChange={(event) => setDescription(event.target.value)}
        />
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="primary" busy={save.isPending} disabled={name.trim() === ''}>
            {group ? 'Save' : 'Create group'}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}

function MembersDialog({ group, onClose }: { group: GroupView; onClose: () => void }) {
  const details = useGroup(group.id);
  const member = useGroupMember(group.id);
  const members = details.data?.members ?? [];
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()} title={`Members of ${group.name}`} wide>
      <div className="flex flex-col gap-4">
        <DirectoryPicker label="Add a person" usersOnly onPick={(entry) => member.add.mutate(entry.id)} />
        {(member.add.isError || member.remove.isError) && (
          <ErrorAlert error={member.add.error ?? member.remove.error} />
        )}
        {details.isPending && <Loading label="Loading members…" />}
        {details.data && members.length === 0 && <p className="text-sm text-muted">Nobody is in this group yet.</p>}
        {members.length > 0 && (
          <ul aria-label="Members" className="divide-y divide-line rounded-control border border-line">
            {members.map((user) => (
              <li key={user.id} className="flex items-center gap-3 px-3 py-2 text-sm">
                <span className="font-medium">{user.userName}</span>
                <span className="min-w-0 flex-1 truncate text-muted">{user.displayName}</span>
                <button
                  type="button"
                  aria-label={`Remove ${user.userName} from ${group.name}`}
                  title="Remove"
                  onClick={() => member.remove.mutate(user.id)}
                  className="rounded-control p-1 text-muted hover:bg-danger-soft hover:text-danger"
                >
                  <X aria-hidden="true" size={14} />
                </button>
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

function DeleteGroupDialog({ group, onClose }: { group: GroupView; onClose: () => void }) {
  const remove = useDeleteGroup(group.id);
  return (
    <ConfirmDialog
      title={`Delete ${group.name}?`}
      open
      onOpenChange={(open) => !open && onClose()}
      confirmLabel="Delete group"
      busy={remove.isPending}
      error={remove.isError ? remove.error : null}
      onConfirm={() => remove.mutate(undefined, { onSuccess: onClose })}
    >
      Its members stay; the roles given to the group on folders end.
    </ConfirmDialog>
  );
}
