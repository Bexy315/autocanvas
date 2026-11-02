import { clsx } from 'clsx';
import type { ViewMode } from './SplitView';

const modes: { mode: ViewMode; label: string }[] = [
  { mode: 'canvas', label: 'Canvas' },
  { mode: 'split', label: 'Split' },
  { mode: 'yaml', label: 'YAML' },
];

/** Canvas, both side by side, or the file alone. */
export function ViewSwitch({ mode, onMode }: { mode: ViewMode; onMode: (mode: ViewMode) => void }) {
  return (
    <fieldset className="flex rounded-control border border-line-strong bg-surface p-0.5">
      <legend className="sr-only">Show</legend>
      {modes.map((entry) => (
        <button
          key={entry.mode}
          type="button"
          aria-pressed={mode === entry.mode}
          onClick={() => onMode(entry.mode)}
          className={clsx(
            'h-7 rounded px-2.5 text-xs',
            mode === entry.mode ? 'bg-accent-soft font-medium text-accent' : 'text-muted hover:text-ink',
          )}
        >
          {entry.label}
        </button>
      ))}
    </fieldset>
  );
}
