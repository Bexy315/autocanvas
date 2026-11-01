import * as Radix from '@radix-ui/react-dialog';
import { X } from 'lucide-react';
import type { ReactNode } from 'react';

/**
 * A modal dialog: focus stays inside while it is open, Esc and the close button dismiss it, and
 * focus returns to what opened it.
 */
export function Dialog({
  open,
  onOpenChange,
  title,
  description,
  children,
  wide = false,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description?: ReactNode;
  children: ReactNode;
  /** For dialogs with more content, such as a folder picker or a list of problems. */
  wide?: boolean;
}) {
  return (
    <Radix.Root open={open} onOpenChange={onOpenChange}>
      <Radix.Portal>
        <Radix.Overlay className="fixed inset-0 z-40 bg-black/40" />
        <Radix.Content
          className={`fixed top-[12vh] left-1/2 z-50 flex max-h-[76vh] w-[calc(100vw-2rem)] -translate-x-1/2 flex-col rounded-panel border border-line bg-surface text-ink shadow-xl ${wide ? 'max-w-2xl' : 'max-w-md'}`}
          {...(description ? {} : { 'aria-describedby': undefined })}
          onOpenAutoFocus={(event) => {
            // The first field rather than the close button, so typing can start at once.
            const field = (event.currentTarget as HTMLElement | null)?.querySelector<HTMLElement>(
              'input, textarea, select',
            );
            if (field) {
              event.preventDefault();
              field.focus();
            }
          }}
        >
          <div className="flex items-start justify-between gap-4 border-b border-line px-5 py-3.5">
            <div className="flex flex-col gap-0.5">
              <Radix.Title className="text-base font-semibold">{title}</Radix.Title>
              {description && <Radix.Description className="text-muted">{description}</Radix.Description>}
            </div>
            <Radix.Close
              aria-label="Close"
              className="-mr-1 rounded-control p-1 text-muted hover:bg-sunken hover:text-ink"
            >
              <X aria-hidden="true" size={16} />
            </Radix.Close>
          </div>
          <div className="overflow-auto px-5 py-4">{children}</div>
        </Radix.Content>
      </Radix.Portal>
    </Radix.Root>
  );
}

/** The row of buttons at the end of a dialog's form: the main action last. */
export function DialogActions({ children }: { children: ReactNode }) {
  return <div className="mt-2 flex justify-end gap-2">{children}</div>;
}
