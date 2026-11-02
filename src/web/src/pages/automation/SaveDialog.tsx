import { Save } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { changeSummary } from '../../lib/diff';
import { ErrorAlert } from '../../ui/Alert';
import { Button } from '../../ui/Button';
import { Dialog, DialogActions } from '../../ui/Dialog';

/**
 * Saving a new version is a step of its own: Ctrl+S and the Save button open this dialog, and
 * Enter in it saves. Pressing Ctrl+S out of habit after every small change costs an Esc instead of
 * leaving a trail of versions. Mounted while open, so each opening starts with an empty message.
 */
export function SaveDialog({
  open,
  onOpenChange,
  version,
  before,
  after,
  blockedReason,
  busy,
  error,
  onSave,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The number the new version gets. */
  version: number;
  before: string;
  after: string;
  blockedReason: string | null;
  busy: boolean;
  error: unknown;
  onSave: (message: string) => void;
}) {
  const [message, setMessage] = useState('');
  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (blockedReason === null && !busy) {
      onSave(message.trim());
    }
  };

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title={`Save version ${version}`}
      description="Runs started after saving use the new version."
    >
      <form onSubmit={submit} className="flex flex-col gap-4">
        {error !== null && <ErrorAlert error={error} title="The file was not saved" />}
        <label className="flex flex-col gap-1.5">
          <span className="text-sm font-medium">Message</span>
          <input
            value={message}
            onChange={(event) => setMessage(event.target.value)}
            placeholder="What changed? (optional)"
            className="h-8 w-full rounded-control border border-line-strong bg-surface px-2.5 text-sm placeholder:text-muted"
          />
        </label>
        <p className="text-sm text-muted">{changeSummary(before, after)}</p>
        {blockedReason !== null && <p className="text-sm text-danger">{blockedReason}</p>}
        <DialogActions>
          <Button onClick={() => onOpenChange(false)}>Cancel</Button>
          <Button type="submit" variant="primary" busy={busy} disabled={blockedReason !== null}>
            <Save aria-hidden="true" size={14} />
            Save version
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}
