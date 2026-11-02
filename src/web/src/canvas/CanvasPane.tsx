import { clsx } from 'clsx';
import { List, Workflow } from 'lucide-react';
import { useState } from 'react';
import { readSetting, writeSetting } from '../app/storage';
import { AutomationCanvas } from './AutomationCanvas';
import { ListView } from './ListView';
import type { Graph } from './model';

export const canvasViewKey = 'autocanvas.editor.canvas';

/** The canvas or its list form, with a note when the file cannot be read right now. */
export function CanvasPane({
  graph,
  stale,
  selectedJob,
  problemsByJob,
  onSelectJob,
}: {
  graph: Graph;
  stale: boolean;
  selectedJob: string | null;
  problemsByJob: Readonly<Record<string, number>>;
  onSelectJob: (id: string) => void;
}) {
  const [asList, setAsList] = useState(() => readSetting(canvasViewKey) === 'list');
  const choose = (list: boolean) => {
    writeSetting(canvasViewKey, list ? 'list' : 'graph');
    setAsList(list);
  };

  return (
    <div className="flex h-full flex-col">
      <div className="flex items-center justify-between gap-2 border-b border-line bg-surface px-2 py-1">
        <span className="px-1 text-xs text-muted">Read-only for now: edit the file</span>
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
          The file cannot be read right now; showing the last readable state.
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
          />
        )}
      </div>
    </div>
  );
}
