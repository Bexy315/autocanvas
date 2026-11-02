/**
 * Values the server shows once (a token, a secret, a password set for someone): with copy buttons,
 * and a dialog that closes only once the user says they saved them, so Esc or a stray click cannot
 * lose them.
 */
import { Copy, TriangleAlert } from 'lucide-react';
import { useId, useState, type ReactNode } from 'react';
import { Button } from './Button';
import { Dialog, DialogActions } from './Dialog';

export function CopyButton({ text, label }: { text: string; label: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      onClick={() =>
        void navigator.clipboard?.writeText(text).then(
          () => setCopied(true),
          () => setCopied(false),
        )
      }
      className="flex shrink-0 items-center gap-1 rounded-control px-2 py-1 text-xs text-muted hover:bg-sunken hover:text-ink"
    >
      <Copy aria-hidden="true" size={12} />
      {copied ? 'Copied' : 'Copy'}
    </button>
  );
}

/** A value in monospace with its label and a copy button. */
export function CopyValue({ label, value }: { label: string; value: string }) {
  const id = useId();
  return (
    <div className="flex flex-col gap-1">
      <span id={id} className="text-sm font-medium">
        {label}
      </span>
      <div className="flex items-center gap-2 rounded-control border border-line-strong bg-sunken px-2 py-1.5">
        <code aria-labelledby={id} className="min-w-0 flex-1 font-mono text-xs break-all">
          {value}
        </code>
        <CopyButton text={value} label={`Copy the ${label.toLowerCase()}`} />
      </div>
    </div>
  );
}

export function OnceDialog({
  title,
  description,
  saveLabel,
  onClose,
  wide = false,
  children,
}: {
  title: string;
  description?: string;
  /** What the user confirms: "I have saved the token". */
  saveLabel: string;
  onClose: () => void;
  wide?: boolean;
  children: ReactNode;
}) {
  const [saved, setSaved] = useState(false);
  return (
    <Dialog
      open
      wide={wide}
      onOpenChange={(open) => !open && saved && onClose()}
      title={title}
      {...(description ? { description } : {})}
    >
      <div className="flex flex-col gap-4">
        <p className="flex gap-2 rounded-control bg-warn-soft px-3 py-2 text-sm">
          <TriangleAlert aria-hidden="true" size={16} className="mt-0.5 shrink-0 text-warn" />
          This is shown only now; AutoCanvas keeps no copy it could show again.
        </p>
        {children}
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={saved}
            onChange={(event) => setSaved(event.target.checked)}
            className="size-4 accent-(--color-accent)"
          />
          {saveLabel}
        </label>
        <DialogActions>
          <Button variant="primary" disabled={!saved} onClick={onClose}>
            Done
          </Button>
        </DialogActions>
      </div>
    </Dialog>
  );
}
