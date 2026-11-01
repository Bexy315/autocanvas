import { useState, type FormEvent } from 'react';
import { ApiError } from '../api/problem';
import { useChangePassword } from '../api/session';
import { ErrorAlert } from '../ui/Alert';
import { Button } from '../ui/Button';
import { TextField } from '../ui/TextField';

/** The server's rule (PasswordPolicy), repeated so people know it before they hit it. */
const rule = '12 to 256 characters, and not your user name.';

/** Changing one's own password; `onChanged` runs once the server took the new one. */
export function PasswordForm({ onChanged, submitLabel }: { onChanged: () => void; submitLabel: string }) {
  const change = useChangePassword();
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmation, setConfirmation] = useState('');
  const [mismatch, setMismatch] = useState(false);

  const fieldErrors = (field: string) => (change.error instanceof ApiError ? change.error.fieldErrors(field) : []);
  const known = fieldErrors('currentPassword').length + fieldErrors('newPassword').length > 0;

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (newPassword !== confirmation) {
      setMismatch(true);
      return;
    }
    setMismatch(false);
    change.mutate(
      { currentPassword, newPassword },
      {
        onSuccess: () => {
          setCurrentPassword('');
          setNewPassword('');
          setConfirmation('');
          onChanged();
        },
      },
    );
  };

  return (
    <form onSubmit={submit} className="flex flex-col gap-4" noValidate>
      {change.isError && !known && <ErrorAlert error={change.error} title="The password was not changed" />}
      <TextField
        label="Current password"
        type="password"
        autoComplete="current-password"
        required
        value={currentPassword}
        errors={fieldErrors('currentPassword')}
        onChange={(event) => setCurrentPassword(event.target.value)}
      />
      <TextField
        label="New password"
        type="password"
        autoComplete="new-password"
        required
        hint={rule}
        value={newPassword}
        errors={fieldErrors('newPassword')}
        onChange={(event) => setNewPassword(event.target.value)}
      />
      <TextField
        label="Repeat the new password"
        type="password"
        autoComplete="new-password"
        required
        value={confirmation}
        errors={mismatch ? ['The two new passwords differ.'] : []}
        onChange={(event) => setConfirmation(event.target.value)}
      />
      <Button
        type="submit"
        variant="primary"
        busy={change.isPending}
        disabled={currentPassword.length === 0 || newPassword.length === 0 || confirmation.length === 0}
        className="mt-1 self-start"
      >
        {submitLabel}
      </Button>
    </form>
  );
}
