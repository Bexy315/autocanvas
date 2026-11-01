import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router';
import { useCreateFolder, useUpdateFolder, type FolderView } from '../api/folders';
import { ApiError } from '../api/problem';
import { ErrorAlert } from '../ui/Alert';
import { Button } from '../ui/Button';
import { Dialog, DialogActions } from '../ui/Dialog';
import { TextField } from '../ui/TextField';

function fieldErrors(error: unknown, field: string): string[] {
  return error instanceof ApiError ? error.fieldErrors(field) : [];
}

/** The fields of a folder, for creating one and for renaming or describing it. */
function FolderForm({
  initial,
  submitLabel,
  busy,
  error,
  onSubmit,
  onCancel,
}: {
  initial: { name: string; description: string };
  submitLabel: string;
  busy: boolean;
  error: unknown;
  onSubmit: (values: { name: string; description: string | null }) => void;
  onCancel: () => void;
}) {
  const [name, setName] = useState(initial.name);
  const [description, setDescription] = useState(initial.description);
  const nameErrors = fieldErrors(error, 'name');
  const other = error !== null && nameErrors.length === 0 && fieldErrors(error, 'description').length === 0;

  const submit = (event: FormEvent) => {
    event.preventDefault();
    onSubmit({ name: name.trim(), description: description.trim() === '' ? null : description.trim() });
  };

  return (
    <form onSubmit={submit} className="flex flex-col gap-4" noValidate>
      {other && <ErrorAlert error={error} />}
      <TextField
        label="Name"
        required
        hint="1 to 100 characters, no slash."
        value={name}
        errors={nameErrors}
        onChange={(event) => setName(event.target.value)}
      />
      <TextField
        label="Description"
        hint="Optional: what belongs in this folder."
        value={description}
        errors={fieldErrors(error, 'description')}
        onChange={(event) => setDescription(event.target.value)}
      />
      <DialogActions>
        <Button onClick={onCancel}>Cancel</Button>
        <Button type="submit" variant="primary" busy={busy} disabled={name.trim().length === 0}>
          {submitLabel}
        </Button>
      </DialogActions>
    </form>
  );
}

/** Creates a folder at the top level or inside `parent`, then opens it. */
export function CreateFolderDialog({
  parent,
  open,
  onOpenChange,
}: {
  parent: { id: string; name: string } | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const create = useCreateFolder();
  const navigate = useNavigate();
  const close = (next: boolean) => {
    if (!next) {
      create.reset();
    }
    onOpenChange(next);
  };

  return (
    <Dialog
      open={open}
      onOpenChange={close}
      title={parent ? `New folder in ${parent.name}` : 'New folder'}
      description={parent ? 'It inherits who may do what from the folder it is in.' : 'You become its owner.'}
    >
      {open && (
        <FolderForm
          initial={{ name: '', description: '' }}
          submitLabel="Create folder"
          busy={create.isPending}
          error={create.error}
          onCancel={() => close(false)}
          onSubmit={(values) =>
            create.mutate(
              { ...values, parentId: parent?.id ?? null },
              {
                onSuccess: (folder) => {
                  close(false);
                  void navigate(`/folders/${folder.id}`);
                },
              },
            )
          }
        />
      )}
    </Dialog>
  );
}

/** Renames a folder or changes its description. */
export function EditFolderDialog({
  folder,
  tag,
  open,
  onOpenChange,
}: {
  folder: FolderView;
  tag: string | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const update = useUpdateFolder(folder.id);
  const close = (next: boolean) => {
    if (!next) {
      update.reset();
    }
    onOpenChange(next);
  };

  return (
    <Dialog open={open} onOpenChange={close} title="Rename folder">
      {open && (
        <FolderForm
          initial={{ name: folder.name, description: folder.description ?? '' }}
          submitLabel="Save"
          busy={update.isPending}
          error={update.error}
          onCancel={() => close(false)}
          onSubmit={(values) => update.mutate({ ...values, tag }, { onSuccess: () => close(false) })}
        />
      )}
    </Dialog>
  );
}
