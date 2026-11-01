import { LoaderCircle } from 'lucide-react';

/** A turning circle for work in progress; decorative, so the surrounding text says what is loading. */
export function Spinner({ size = 16 }: { size?: number }) {
  return <LoaderCircle aria-hidden="true" size={size} className="animate-spin" />;
}

/** A centered loading state for a whole area, announced to screen readers. */
export function Loading({ label = 'Loading…' }: { label?: string }) {
  return (
    <output className="flex items-center justify-center gap-2 p-8 text-muted">
      <Spinner />
      <span>{label}</span>
    </output>
  );
}
