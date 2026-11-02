import { clsx } from 'clsx';
import { LayoutGrid, List, Redo2, Undo2, Workflow } from 'lucide-react';
import { useState } from 'react';
import { readSetting, writeSetting } from '../app/storage';
import { AutomationCanvas, type CanvasEdits } from './AutomationCanvas';
import { ListView } from './ListView';
import type { Graph } from './model';
import type { NodeTypeView } from './operations';

export const canvasViewKey = 'autocanvas.editor.canvas';

const noNodeTypes: readonly NodeTypeView[] = [];

/**
 * The canvas or its list form, with a note when the file cannot be read right now. For editors it
 * also offers undo, redo and tidying up, and announces each change made on the canvas.
 */
export function CanvasPane({
  graph,
  stale,
  selectedJob,
  problemsByJob,
  onSelectJob,
  edits,
  onTidy,
  paletteNodeTypes = noNodeTypes,
  announcement = '',
  fitKey = 0,
}: {
  graph: Graph;
  stale: boolean;
  selectedJob: string | null;
  problemsByJob: Readonly<Record<string, number>>;
  onSelectJob: (id: string) => void;
  /** For editors: the canvas changes the file. */
  edits?: CanvasEdits;
  onTidy?: () => void;
  paletteNodeTypes?: readonly NodeTypeView[];
  /** The last change made on the canvas, in words, for screen readers. */
  announcement?: string;
  /** Changes after Tidy up, to fit the view to the new layout. */
  fitKey?: number;
}) {
  const [asList, setAsList] = useState(() => readSetting(canvasViewKey) === 'list');
  const choose = (list: boolean) => {
    writeSetting(canvasViewKey, list ? 'list' : 'graph');
    setAsList(list);
  };

  return (
    <div className="flex h-full flex-col">
      <div className="flex items-center justify-between gap-2 border-b border-line bg-surface px-2 py-1">
        {edits ? (
          <div className="flex items-center gap-0.5">
            <ToolButton label="Undo (Ctrl+Z)" onClick={edits.undo} Icon={Undo2} />
            <ToolButton label="Redo (Ctrl+Y)" onClick={edits.redo} Icon={Redo2} />
            <ToolButton label="Tidy up" onClick={() => onTidy?.()} Icon={LayoutGrid} disabled={stale} text />
          </div>
        ) : (
          <span className="px-1 text-xs text-muted">You can read the canvas; changing it needs the Editor role.</span>
        )}
        <fieldset className="flex rounded-control border border-line p-0.5">
          <legend className="sr-only">Show the automation as</legend>
          {[
            { list: false, label: 'Graph', Icon: Workflow },
            { list: true, label: 'List', Icon: List },
          ].map(({ list, label, Icon }) => (
            <button
              key={label}
              type="button"
              aria-pressed={asList === list}
              onClick={() => choose(list)}
              className={clsx(
                'flex h-6 items-center gap-1 rounded px-2 text-xs',
                asList === list ? 'bg-accent-soft font-medium text-accent' : 'text-muted hover:text-ink',
              )}
            >
              <Icon aria-hidden="true" size={12} />
              {label}
            </button>
          ))}
        </fieldset>
      </div>
      {stale && (
        <output className="block border-b border-line bg-warn-soft px-3 py-1.5 text-xs text-ink">
          {edits
            ? 'The file cannot be read right now; fix it in the YAML to edit on the canvas again.'
            : 'The file cannot be read right now; showing the last readable state.'}
        </output>
      )}
      <div className="min-h-0 flex-1">
        {asList ? (
          <ListView graph={graph} selectedJob={selectedJob} onSelectJob={onSelectJob} />
        ) : (
          <AutomationCanvas
            graph={graph}
            stale={stale}
            selectedJob={selectedJob}
            problemsByJob={problemsByJob}
            onSelectJob={onSelectJob}
            fitKey={fitKey}
            {...(edits ? { edits, paletteNodeTypes } : {})}
          />
        )}
      </div>
      <output aria-live="polite" className="sr-only">
        {announcement}
      </output>
    </div>
  );
}

function ToolButton({
  label,
  onClick,
  Icon,
  disabled = false,
  text = false,
}: {
  label: string;
  onClick: () => void;
  Icon: typeof Undo2;
  disabled?: boolean;
  /** Show the label next to the icon instead of only as a tooltip. */
  text?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      title={label}
      aria-label={text ? undefined : label}
      className="flex h-7 items-center gap-1 rounded-control px-1.5 text-xs text-muted hover:bg-sunken hover:text-ink disabled:opacity-50"
    >
      <Icon aria-hidden="true" size={14} />
      {text && label}
    </button>
  );
}
