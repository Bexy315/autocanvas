import { useState, type ChangeEvent, type FormEvent } from 'react';
import { useNavigate } from 'react-router';
import { useCreateAutomation, useRenameAutomation, type AutomationView } from '../api/automations';
import { ApiError } from '../api/problem';
import { ErrorAlert } from '../ui/Alert';
import { Button } from '../ui/Button';
import { DiagnosticList } from '../ui/DiagnosticList';
import { Dialog, DialogActions } from '../ui/Dialog';
import { TextField } from '../ui/TextField';
import { starterFile } from './starter';

function fieldErrors(error: unknown, field: string): string[] {
  return error instanceof ApiError ? error.fieldErrors(field) : [];
}

const nameHint = 'Like a file name: 1 to 100 characters, no slash, unique in the folder.';

/** Creates an automation in a folder, from the starter file or an uploaded one, then opens it. */
export function CreateAutomationDialog({
  folder,
  open,
  onOpenChange,
}: {
  folder: { id: string; name: string };
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const create = useCreateAutomation(folder.id);
  const navigate = useNavigate();
  const [name, setName] = useState('');
  const [source, setSource] = useState<'starter' | 'upload'>('starter');
  const [upload, setUpload] = useState<{ fileName: string; text: string } | null>(null);
  const [message, setMessage] = useState('');

  const close = (next: boolean) => {
    if (!next) {
      create.reset();
      setName('');
      setSource('starter');
      setUpload(null);
      setMessage('');
    }
    onOpenChange(next);
  };

  const pick = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    create.reset();
    setUpload(file ? { fileName: file.name, text: await file.text() } : null);
    if (file && name === '') {
      setName(file.name.replace(/\.ya?ml$/i, ''));
    }
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const yaml = source === 'starter' ? starterFile(name) : (upload?.text ?? '');
    create.mutate(
      { name: name.trim(), yaml, message: message.trim() === '' ? null : message.trim() },
      {
        onSuccess: (automation) => {
          close(false);
          void navigate(`/automations/${automation.id}`);
        },
      },
    );
  };

  const problem = create.error instanceof ApiError ? create.error.problem : null;
  const diagnostics = problem?.diagnostics ?? [];
  const nameErrors = fieldErrors(create.error, 'name');
  const fileErrors = diagnostics.length > 0 ? [] : fieldErrors(create.error, 'yaml');
  const other = create.isError && nameErrors.length === 0 && fileErrors.length === 0 && diagnostics.length === 0;
  const ready = name.trim().length > 0 && (source === 'starter' || upload !== null);

  return (
    <Dialog open={open} onOpenChange={close} title={`New automation in ${folder.name}`} wide>
      <form onSubmit={submit} className="flex flex-col gap-4" noValidate>
        {other && <ErrorAlert error={create.error} />}
        <TextField
          label="Name"
          required
          hint={nameHint}
          value={name}
          errors={nameErrors}
          onChange={(event) => setName(event.target.value)}
        />
        <fieldset className="flex flex-col gap-2">
          <legend className="mb-1 text-sm font-medium">Start with</legend>
          <label className="flex items-start gap-2">
            <input
              type="radio"
              name="source"
              className="mt-1 accent-accent"
              checked={source === 'starter'}
              onChange={() => setSource('starter')}
            />
            <span>
              The starter file
              <span className="block text-xs text-muted">Started by hand, one job that says hello.</span>
            </span>
          </label>
          <label className="flex items-start gap-2">
            <input
              type="radio"
              name="source"
              className="mt-1 accent-accent"
              checked={source === 'upload'}
              onChange={() => setSource('upload')}
            />
            <span>
              A file from your computer
              <span className="block text-xs text-muted">
                An automation file (.yaml or .yml), e.g. from a repository.
              </span>
            </span>
          </label>
          {source === 'upload' && (
            <div className="ml-6 flex flex-col gap-1">
              <label className="text-sm font-medium" htmlFor="automation-file">
                Automation file
              </label>
              <input
                id="automation-file"
                type="file"
                accept=".yaml,.yml,application/yaml,text/yaml"
                onChange={(event) => void pick(event)}
                className="text-sm file:mr-3 file:h-8 file:rounded-control file:border file:border-line-strong file:bg-surface file:px-3 file:text-ink"
              />
              {fileErrors.length > 0 && <p className="text-xs text-danger">{fileErrors.join(' ')}</p>}
            </div>
          )}
        </fieldset>
        {diagnostics.length > 0 && (
          <div className="flex flex-col gap-2">
            <p className="text-sm text-danger" role="alert">
              {problem?.detail ?? 'The file has problems.'} Fix them in the file and choose it again.
            </p>
            <DiagnosticList diagnostics={diagnostics} label="Problems in the file" />
          </div>
        )}
        <TextField
          label="Message"
          hint="Optional: why this automation exists. Kept with version 1."
          value={message}
          onChange={(event) => setMessage(event.target.value)}
        />
        <DialogActions>
          <Button onClick={() => close(false)}>Cancel</Button>
          <Button type="submit" variant="primary" busy={create.isPending} disabled={!ready}>
            Create automation
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}

/** Changes an automation's name (its key in the folder); the title comes from the file. */
export function RenameAutomationDialog({
  automation,
  tag,
  open,
  onOpenChange,
}: {
  automation: AutomationView;
  tag: string | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const rename = useRenameAutomation(automation.id);
  const [name, setName] = useState(automation.name);
  const close = (next: boolean) => {
    if (!next) {
      rename.reset();
      setName(automation.name);
    }
    onOpenChange(next);
  };
  const nameErrors = fieldErrors(rename.error, 'name');

  return (
    <Dialog
      open={open}
      onOpenChange={close}
      title="Rename automation"
      description="The name is its key in the folder. The title shown in lists comes from name: in the file."
    >
      <form
        onSubmit={(event) => {
          event.preventDefault();
          rename.mutate({ name: name.trim(), tag }, { onSuccess: () => close(false) });
        }}
        className="flex flex-col gap-4"
        noValidate
      >
        {rename.isError && nameErrors.length === 0 && <ErrorAlert error={rename.error} />}
        <TextField
          label="Name"
          required
          hint={nameHint}
          value={name}
          errors={nameErrors}
          onChange={(event) => setName(event.target.value)}
        />
        <DialogActions>
          <Button onClick={() => close(false)}>Cancel</Button>
          <Button type="submit" variant="primary" busy={rename.isPending} disabled={name.trim().length === 0}>
            Rename
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}
