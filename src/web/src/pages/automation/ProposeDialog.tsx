import { GitPullRequest } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { changeSummary } from '../../lib/diff';
import { ErrorAlert } from '../../ui/Alert';
import { Button } from '../../ui/Button';
import { Dialog, DialogActions } from '../../ui/Dialog';

/**
 * A repository automation is changed in its repository: instead of a new version, the change
 * becomes a pull request there (GitHub). The title and commit message are optional.
 */
export function ProposeDialog({
  repository,
  path,
  before,
  after,
  blockedReason,
  busy,
  error,
  onOpenChange,
  onPropose,
}: {
  repository: string;
  path: string;
  before: string;
  after: string;
  blockedReason: string | null;
  busy: boolean;
  error: unknown;
  onOpenChange: (open: boolean) => void;
  onPropose: (title: string, message: string) => void;
}) {
  const [title, setTitle] = useState('');
  const [message, setMessage] = useState('');
  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (blockedReason === null && !busy) {
      onPropose(title.trim(), message.trim());
    }
  };

  return (
    <Dialog
      open
      onOpenChange={onOpenChange}
      title="Propose as a pull request"
      description={`The automation lives in ${repository} (${path}). The change becomes a branch and a pull request there; the automation changes here once it is merged.`}
    >
      <form onSubmit={submit} className="flex flex-col gap-4">
        {error !== null && <ErrorAlert error={error} title="The change was not proposed" />}
        <label className="flex flex-col gap-1.5">
          <span className="text-sm font-medium">Title</span>
          <input
            value={title}
            onChange={(event) => setTitle(event.target.value)}
            placeholder="The commit message (optional)"
            className="h-8 w-full rounded-control border border-line-strong bg-surface px-2.5 text-sm placeholder:text-muted"
          />
        </label>
        <label className="flex flex-col gap-1.5">
          <span className="text-sm font-medium">Commit message</span>
          <input
            value={message}
            onChange={(event) => setMessage(event.target.value)}
            placeholder="Change … in AutoCanvas (optional)"
            className="h-8 w-full rounded-control border border-line-strong bg-surface px-2.5 text-sm placeholder:text-muted"
          />
        </label>
        <p className="text-sm text-muted">{changeSummary(before, after)}</p>
        {blockedReason !== null && <p className="text-sm text-danger">{blockedReason}</p>}
        <DialogActions>
          <Button onClick={() => onOpenChange(false)}>Cancel</Button>
          <Button type="submit" variant="primary" busy={busy} disabled={blockedReason !== null}>
            <GitPullRequest aria-hidden="true" size={14} />
            Open pull request
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}
