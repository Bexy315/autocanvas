import { useState, type FormEvent } from 'react';
import { ApiError } from '../api/problem';
import { ErrorAlert } from '../ui/Alert';
import { Button } from '../ui/Button';
import { Dialog, DialogActions } from '../ui/Dialog';
import { TextField } from '../ui/TextField';

/** What a variable's or secret's name may look like; the server checks it the same way. */
const namePattern = /^[A-Za-z_][A-Za-z0-9_]*$/;

function fieldErrors(error: unknown, field: string): string[] {
  return error instanceof ApiError ? error.fieldErrors(field) : [];
}

/**
 * Sets a variable or a secret: a new one with its name, or the value of one that exists. A secret's
 * value is typed into a password field and never shown, not even the one it replaces.
 */
export function ValueDialog({
  open,
  onOpenChange,
  kind,
  name: existing,
  initialValue = '',
  busy,
  error,
  onSave,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  kind: 'variable' | 'secret';
  /** The name of the one being changed; null for a new one. */
  name: string | null;
  initialValue?: string;
  busy: boolean;
  error: unknown;
  onSave: (name: string, value: string) => void;
}) {
  const [name, setName] = useState(existing ?? '');
  const [value, setValue] = useState(initialValue);
  const nameErrors = [
    ...(name !== '' && !namePattern.test(name) ? ['Letters, digits and _, not starting with a digit.'] : []),
    ...fieldErrors(error, 'name'),
  ];
  const valueErrors = fieldErrors(error, 'value');
  const other =
    error !== null && error !== undefined && fieldErrors(error, 'name').length === 0 && valueErrors.length === 0;
  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (namePattern.test(name.trim())) {
      onSave(name.trim(), value);
    }
  };
  const title = existing
    ? kind === 'secret'
      ? `Replace the secret ${existing}`
      : `Change the variable ${existing}`
    : kind === 'secret'
      ? 'Add a secret'
      : 'Add a variable';

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={title}
      {...(kind === 'secret'
        ? { description: 'The value is stored encrypted. Nobody can read it back, not even here.' }
        : {})}
    >
      <form onSubmit={submit} className="flex flex-col gap-4" noValidate>
        {other && <ErrorAlert error={error} />}
        {existing === null && (
          <TextField
            label="Name"
            required
            value={name}
            hint={kind === 'secret' ? 'Read as ${{ secrets.NAME }}.' : 'Read as ${{ vars.NAME }}.'}
            errors={nameErrors}
            onChange={(event) => setName(event.target.value)}
            className="font-mono"
          />
        )}
        <TextField
          label="Value"
          type={kind === 'secret' ? 'password' : 'text'}
          autoComplete={kind === 'secret' ? 'new-password' : 'off'}
          value={value}
          errors={valueErrors}
          onChange={(event) => setValue(event.target.value)}
        />
        <DialogActions>
          <Button onClick={() => onOpenChange(false)}>Cancel</Button>
          <Button type="submit" variant="primary" busy={busy} disabled={!namePattern.test(name.trim())}>
            {existing ? 'Save' : kind === 'secret' ? 'Add secret' : 'Add variable'}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}
