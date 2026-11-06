import { clsx } from 'clsx';
import { PanelLeftClose, PanelLeftOpen, Search } from 'lucide-react';
import { useId, useState } from 'react';
import { readSetting, writeSetting } from '../app/storage';
import type { NewJobKind, NodeTypeView } from './operations';
import { flowEntries, nodeTypeIcon, paletteMime, type Entry } from './paletteEntries';

const openSetting = 'autocanvas.canvas.palette';

/**
 * What can be added to the canvas, in a panel at its left edge: drag an entry to a place, or choose
 * it (click, Enter) to add it in the middle of the view. Flow elements come first, then the node
 * types, which grow with plugins; a filter narrows both. The panel can be hidden to make room, and
 * the browser remembers that.
 */
export function Palette({
  nodeTypes,
  onAdd,
  compact = false,
}: {
  nodeTypes: readonly NodeTypeView[];
  onAdd: (kind: NewJobKind) => void;
  /** Icons only, whatever the room: while the settings of a card take the space beside the canvas. */
  compact?: boolean;
}) {
  const [open, setOpen] = useState(() => readSetting(openSetting) !== 'closed');
  const [filter, setFilter] = useState('');
  const filterId = useId();
  const show = (value: boolean) => {
    setOpen(value);
    writeSetting(openSetting, value ? 'open' : 'closed');
  };

  if (!open) {
    return (
      <div className="flex shrink-0 flex-col items-center border-r border-line bg-surface py-1">
        <button
          type="button"
          onClick={() => show(true)}
          aria-label="Show what can be added"
          title="Show what can be added"
          className="rounded-control p-1.5 text-muted hover:bg-sunken hover:text-ink"
        >
          <PanelLeftOpen aria-hidden="true" size={15} />
        </button>
      </div>
    );
  }

  const nodeEntries: Entry[] = nodeTypes.map((type) => ({
    key: `node:${type.name}`,
    label: type.name,
    description: type.description,
    Icon: nodeTypeIcon(type.name),
    kind: { nodeType: type },
  }));
  const wanted = filter.trim().toLowerCase();
  const matches = (entry: Entry) =>
    wanted === '' || entry.label.toLowerCase().includes(wanted) || entry.description.toLowerCase().includes(wanted);
  const groups = [
    { title: 'Flow', entries: flowEntries.filter(matches) },
    { title: 'Node types', entries: nodeEntries.filter(matches) },
  ].filter((group) => group.entries.length > 0);

  // Narrow, or next to the open settings, it is a bar of icons: the name and description stay in
  // the tooltip and the accessible name. With room, entries show both and the filter appears.
  // (Container queries need whole class names in the source, hence the pairs.)
  const wide = !compact;
  return (
    <section
      aria-label="Add to the canvas"
      className={clsx(
        'flex shrink-0 flex-col border-r border-line bg-surface',
        wide ? 'w-11 @2xl/canvas:w-56' : 'w-11',
      )}
    >
      <div
        className={clsx(
          'flex items-center justify-center pt-2',
          wide && '@2xl/canvas:justify-between @2xl/canvas:gap-2 @2xl/canvas:px-3',
        )}
      >
        <h2
          className={clsx(
            'text-xs font-semibold tracking-wide text-muted uppercase',
            wide ? 'sr-only @2xl/canvas:not-sr-only' : 'sr-only',
          )}
        >
          Add
        </h2>
        <button
          type="button"
          onClick={() => show(false)}
          aria-label="Hide what can be added"
          title="Hide what can be added"
          className="rounded-control p-1 text-muted hover:bg-sunken hover:text-ink"
        >
          <PanelLeftClose aria-hidden="true" size={15} />
        </button>
      </div>
      <div className={clsx('relative px-2 pt-2 pb-1', wide ? 'hidden @2xl/canvas:block' : 'hidden')}>
        <label htmlFor={filterId} className="sr-only">
          Filter what can be added
        </label>
        <Search
          aria-hidden="true"
          size={13}
          className="pointer-events-none absolute top-1/2 left-4 mt-0.5 text-muted"
        />
        <input
          id={filterId}
          type="search"
          value={filter}
          onChange={(event) => setFilter(event.target.value)}
          placeholder="Filter"
          className="h-7 w-full rounded-control border border-line bg-canvas pr-2 pl-7 text-xs"
        />
      </div>
      <div className="min-h-0 flex-1 overflow-y-auto px-1.5 pb-2">
        {groups.length === 0 && <p className="px-1 py-2 text-xs text-muted">Nothing matches “{filter.trim()}”.</p>}
        {groups.map((group) => (
          <div key={group.title} className="pt-2">
            <h3
              className={clsx(
                'px-1 pb-1 text-[11px] font-medium text-muted',
                wide ? 'sr-only @2xl/canvas:not-sr-only @2xl/canvas:block' : 'sr-only',
              )}
            >
              {group.title}
            </h3>
            <ul className="flex flex-col gap-1">
              {group.entries.map(({ key, label, description, Icon, kind }) => (
                <li key={key}>
                  <button
                    type="button"
                    draggable
                    onDragStart={(event) => {
                      event.dataTransfer.setData(paletteMime, key);
                      event.dataTransfer.effectAllowed = 'copy';
                    }}
                    onClick={() => onAdd(kind)}
                    title={`${label}: ${description}. Drag it to a place, or choose it to add it in the middle.`}
                    aria-label={`Add ${label}: ${description}`}
                    className={clsx(
                      'flex w-full cursor-grab items-center justify-center rounded-control border border-line bg-surface py-1.5 hover:border-accent hover:bg-sunken',
                      wide &&
                        '@2xl/canvas:items-start @2xl/canvas:justify-start @2xl/canvas:gap-2 @2xl/canvas:px-2 @2xl/canvas:text-left',
                    )}
                  >
                    <Icon aria-hidden="true" size={14} className="shrink-0 text-muted @2xl/canvas:mt-0.5" />
                    <span className={clsx('min-w-0', wide ? 'sr-only @2xl/canvas:not-sr-only' : 'sr-only')}>
                      <span className="block truncate text-xs font-medium">{label}</span>
                      <span className="block text-[11px] leading-snug text-muted">{description}</span>
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          </div>
        ))}
      </div>
    </section>
  );
}
