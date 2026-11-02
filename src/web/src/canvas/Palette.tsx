import { Plug } from 'lucide-react';
import type { NewJobKind, NodeTypeView } from './operations';
import { flowEntries, paletteMime, type Entry } from './paletteEntries';

/**
 * What can be added to the canvas: drag an entry to a place, or choose it (click, Enter) to add it
 * in the middle of the view.
 */
export function Palette({
  nodeTypes,
  onAdd,
}: {
  nodeTypes: readonly NodeTypeView[];
  onAdd: (kind: NewJobKind) => void;
}) {
  const entries: Entry[] = [
    ...flowEntries,
    ...nodeTypes.map((type) => ({
      key: `node:${type.name}`,
      label: type.name,
      description: type.description,
      Icon: Plug,
      kind: { nodeType: type } as NewJobKind,
    })),
  ];

  return (
    <div
      role="toolbar"
      aria-label="Add to the canvas"
      className="flex flex-wrap items-center gap-0.5 border-b border-line bg-surface px-2 py-1"
    >
      {entries.map(({ key, label, description, Icon, kind }, index) => (
        <button
          key={key}
          type="button"
          draggable
          onDragStart={(event) => {
            event.dataTransfer.setData(paletteMime, key);
            event.dataTransfer.effectAllowed = 'copy';
          }}
          onClick={() => onAdd(kind)}
          title={`${description}. Drag it to a place, or choose it to add it in the middle.`}
          aria-label={`Add ${label}: ${description}`}
          className={`flex h-7 cursor-grab items-center gap-1.5 rounded-control border border-line px-2 text-xs hover:bg-sunken ${index === flowEntries.length ? 'ml-2' : ''}`}
        >
          <Icon aria-hidden="true" size={13} className="shrink-0 text-muted" />
          <span className="truncate">{label}</span>
        </button>
      ))}
    </div>
  );
}
