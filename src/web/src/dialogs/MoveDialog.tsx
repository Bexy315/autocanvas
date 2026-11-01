import { useState } from 'react';
import { FolderTree, type TreeFolder } from '../layout/FolderTree';
import { hasRole } from '../lib/roles';
import { Alert, ErrorAlert } from '../ui/Alert';
import { Button } from '../ui/Button';
import { Dialog, DialogActions } from '../ui/Dialog';

/** Where to move something: a folder, or the top level when that is allowed. */
export type MoveTarget = { id: string; name: string } | 'top';

/**
 * Picks a new folder for a folder or an automation. Only folders where the user is an Editor can be
 * picked; `excludeId` (the folder being moved) cannot be its own parent. The server checks again,
 * including folders below the moved one.
 */
export function MoveDialog({
  title,
  open,
  onOpenChange,
  currentFolderId,
  excludeId,
  allowTop,
  busy,
  error,
  onMove,
}: {
  title: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Where it is now: not a target. */
  currentFolderId: string | null;
  excludeId?: string;
  /** Whether "Top level" is offered (folders, for their owners). */
  allowTop: boolean;
  busy: boolean;
  error: unknown;
  onMove: (target: MoveTarget) => void;
}) {
  const [target, setTarget] = useState<MoveTarget | null>(null);
  const close = (next: boolean) => {
    if (!next) {
      setTarget(null);
    }
    onOpenChange(next);
  };
  const canChoose = (folder: TreeFolder) =>
    hasRole(folder.myRole, 'Editor') && folder.id !== excludeId && folder.id !== currentFolderId;
  const targetName = target === 'top' ? 'the top level' : target?.name;

  return (
    <Dialog
      open={open}
      onOpenChange={close}
      title={title}
      description="Who may see and change it comes from the folder it is in, so moving it changes who can access it."
      wide
    >
      <div className="flex flex-col gap-4">
        {error !== null && <ErrorAlert error={error} title="It was not moved" />}
        <div className="max-h-72 overflow-auto rounded-control border border-line p-1">
          {allowTop && (
            <button
              type="button"
              onClick={() => setTarget('top')}
              aria-pressed={target === 'top'}
              className={`mb-1 flex h-7 w-full items-center rounded-control px-2 text-left text-sm ${target === 'top' ? 'bg-accent-soft font-medium text-accent' : 'hover:bg-sunken'}`}
            >
              Top level
            </button>
          )}
          {open && (
            <FolderTree
              label="Target folder"
              selectedId={target === 'top' ? undefined : target?.id}
              onChoose={(folder) => setTarget({ id: folder.id, name: folder.name })}
              canChoose={canChoose}
            />
          )}
        </div>
        <p className="text-xs text-muted">Folders where you are not an editor are shown but cannot be picked.</p>
        {target && <Alert tone="info">It moves to {targetName}.</Alert>}
        <DialogActions>
          <Button onClick={() => close(false)}>Cancel</Button>
          <Button variant="primary" busy={busy} disabled={target === null} onClick={() => target && onMove(target)}>
            Move here
          </Button>
        </DialogActions>
      </div>
    </Dialog>
  );
}
