import type { ReactNode } from 'react';
import { ErrorAlert } from '../ui/Alert';
import { Button } from '../ui/Button';
import { Dialog, DialogActions } from '../ui/Dialog';

/** Asks before something that cannot be undone; the button names exactly what happens. */
export function ConfirmDialog({
  title,
  open,
  onOpenChange,
  children,
  confirmLabel,
  busy,
  error,
  onConfirm,
}: {
  title: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  children: ReactNode;
  confirmLabel: string;
  busy: boolean;
  error: unknown;
  onConfirm: () => void;
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange} title={title}>
      <div className="flex flex-col gap-4">
        {error !== null && <ErrorAlert error={error} />}
        <div className="text-ink">{children}</div>
        <DialogActions>
          <Button onClick={() => onOpenChange(false)}>Cancel</Button>
          <Button variant="danger" busy={busy} onClick={onConfirm}>
            {confirmLabel}
          </Button>
        </DialogActions>
      </div>
    </Dialog>
  );
}
