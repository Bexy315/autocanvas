import { collapse, lineDiff } from '../lib/diff';
import { ErrorAlert } from '../ui/Alert';
import { Button } from '../ui/Button';
import { Dialog, DialogActions } from '../ui/Dialog';
import { DiffView } from '../ui/DiffView';
import { Loading } from '../ui/Spinner';

export interface Theirs {
  yaml: string;
  version: number;
  tag: string | null;
}

/**
 * Someone saved while you were editing. Shows what your file changes compared with theirs, and
 * lets you save yours on top or take theirs; nothing is lost without a choice.
 */
export function ConflictDialog({
  open,
  mine,
  theirs,
  loadError,
  busy,
  onSaveMine,
  onLoadTheirs,
  onCancel,
}: {
  open: boolean;
  mine: string;
  theirs: Theirs | null;
  loadError: unknown;
  busy: boolean;
  onSaveMine: () => void;
  onLoadTheirs: () => void;
  onCancel: () => void;
}) {
  const hunks = theirs ? collapse(lineDiff(theirs.yaml, mine)) : [];

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => !next && onCancel()}
      title="Someone saved in the meantime"
      description={
        theirs
          ? `Version ${theirs.version} was saved while you were editing. Below is what your file changes compared with it.`
          : 'A newer version was saved while you were editing.'
      }
      wide
    >
      <div className="flex flex-col gap-4">
        {loadError !== null && <ErrorAlert error={loadError} title="The newer version could not be loaded" />}
        {!theirs && loadError === null && <Loading label="Loading the newer version…" />}
        {theirs && hunks.length === 0 && <p className="text-muted">Your file is the same as theirs.</p>}
        {theirs && hunks.length > 0 && (
          <DiffView hunks={hunks} label={`Your changes compared with version ${theirs.version}`} />
        )}
        <DialogActions>
          <Button onClick={onCancel}>Keep editing</Button>
          <Button variant="danger" disabled={!theirs || busy} onClick={onLoadTheirs}>
            Load theirs
          </Button>
          <Button variant="primary" busy={busy} disabled={!theirs} onClick={onSaveMine}>
            Save mine on top
          </Button>
        </DialogActions>
      </div>
    </Dialog>
  );
}
