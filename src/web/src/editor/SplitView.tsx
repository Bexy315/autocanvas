import { clsx } from 'clsx';
import { useRef, type KeyboardEvent, type PointerEvent, type ReactNode } from 'react';

export type ViewMode = 'canvas' | 'split' | 'yaml';

const minimum = 0.2;
const maximum = 0.8;
const clamp = (value: number) => Math.min(maximum, Math.max(minimum, value));

/**
 * Two panes side by side with a separator that the mouse and the arrow keys move. Both panes stay
 * mounted when one is hidden, so the editor keeps its undo history and the canvas its viewport.
 */
export function SplitView({
  mode,
  ratio,
  onRatio,
  left,
  right,
  leftLabel,
  rightLabel,
}: {
  mode: ViewMode;
  /** The left pane's share of the width, 0.2 to 0.8. */
  ratio: number;
  onRatio: (ratio: number) => void;
  left: ReactNode;
  right: ReactNode;
  leftLabel: string;
  rightLabel: string;
}) {
  const host = useRef<HTMLDivElement>(null);
  const dragging = useRef(false);

  const move = (event: PointerEvent<HTMLDivElement>) => {
    const box = host.current?.getBoundingClientRect();
    if (dragging.current && box && box.width > 0) {
      onRatio(clamp((event.clientX - box.left) / box.width));
    }
  };

  const key = (event: KeyboardEvent<HTMLDivElement>) => {
    const steps: Record<string, number> = { ArrowLeft: -0.05, ArrowRight: 0.05 };
    const step = steps[event.key];
    if (step !== undefined) {
      event.preventDefault();
      onRatio(clamp(ratio + step));
    } else if (event.key === 'Home' || event.key === 'End') {
      event.preventDefault();
      onRatio(event.key === 'Home' ? minimum : maximum);
    }
  };

  const showLeft = mode !== 'yaml';
  const showRight = mode !== 'canvas';
  return (
    <div ref={host} className="flex h-full min-h-0">
      <section
        aria-label={leftLabel}
        className={clsx('min-w-0', showLeft ? 'block' : 'hidden', mode === 'canvas' && 'flex-1')}
        style={mode === 'split' ? { width: `${ratio * 100}%` } : undefined}
      >
        {left}
      </section>
      {mode === 'split' && (
        <div
          // The ARIA window splitter: a focusable separator moved with the arrow keys, which <hr> cannot be.
          // oxlint-disable-next-line jsx-a11y/prefer-tag-over-role
          role="separator"
          aria-orientation="vertical"
          aria-label="Resize canvas and file"
          aria-valuemin={minimum * 100}
          aria-valuemax={maximum * 100}
          aria-valuenow={Math.round(ratio * 100)}
          tabIndex={0}
          onKeyDown={key}
          onPointerDown={(event) => {
            dragging.current = true;
            event.currentTarget.setPointerCapture(event.pointerId);
          }}
          onPointerMove={move}
          onPointerUp={(event) => {
            dragging.current = false;
            event.currentTarget.releasePointerCapture(event.pointerId);
          }}
          className="w-1.5 shrink-0 cursor-col-resize bg-line hover:bg-accent focus-visible:bg-accent"
        />
      )}
      <section aria-label={rightLabel} className={clsx('min-w-0 flex-1', showRight ? 'block' : 'hidden')}>
        {right}
      </section>
    </div>
  );
}
