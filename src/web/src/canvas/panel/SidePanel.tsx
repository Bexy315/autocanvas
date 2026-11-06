import { clsx } from 'clsx';
import { Maximize2, Minimize2, Trash2, X } from 'lucide-react';
import { useMemo, useState, type ReactNode, type Ref } from 'react';
import type { Diagnostic } from '../../api/problem';
import { triggerId, type Graph } from '../model';
import type { NodeTypeView } from '../operations';
import { readPanelFile } from '../panelModel';
import { AutomationPanel } from './AutomationPanel';
import { noReferences, type References } from './references';
import { JobPanel } from './JobPanel';
import { PanelContext, type PanelEdits } from './panelContext';

/** What the side panel shows: one card, or how many are chosen. */
export type PanelSubject = { id: string } | { count: number };

const kinds = { step: 'Job', switch: 'Switch', join: 'Join' } as const;

/**
 * The side panel of the canvas: the chosen card's settings as a form. It reads from the file the
 * canvas shows and changes it through `edits`; without them (viewers, a file that cannot be read)
 * it only shows.
 */
export function SidePanel({
  subject,
  text,
  graph,
  nodeTypes,
  diagnostics,
  edits,
  references = noReferences,
  onClose,
  headingRef,
}: {
  subject: PanelSubject;
  text: string;
  graph: Graph;
  nodeTypes: readonly NodeTypeView[];
  diagnostics: readonly Diagnostic[];
  edits?: PanelEdits | undefined;
  /** The variables and secrets that reach the automation; the file's own `variables:` are added. */
  references?: References | undefined;
  onClose: () => void;
  headingRef?: Ref<HTMLHeadingElement>;
}) {
  const [wide, setWide] = useState(false);
  const file = useMemo(() => readPanelFile(text), [text]);
  const settings = useMemo(() => {
    const own = file?.get(['variables']);
    const variables = new Set([...references.variables, ...(own && typeof own === 'object' ? Object.keys(own) : [])]);
    return {
      readOnly: edits === undefined,
      diagnostics,
      references: { variables: [...variables].toSorted(), secrets: references.secrets.toSorted() },
    };
  }, [edits, diagnostics, file, references]);
  const id = 'id' in subject ? subject.id : null;
  const job = id === null ? undefined : graph.jobs.find((entry) => entry.id === id);
  const title =
    id === triggerId
      ? 'Automation'
      : job
        ? job.title
        : 'count' in subject
          ? `${subject.count} cards chosen`
          : (id ?? '');
  const kind = id === triggerId ? 'Trigger and settings' : job ? (job.isFinally ? 'Finally job' : kinds[job.kind]) : '';

  let body: ReactNode;
  if (!file) {
    body = <p className="p-4 text-sm text-muted">The file cannot be read right now.</p>;
  } else if ('count' in subject) {
    body = <p className="p-4 text-sm text-muted">Choose one card to see its settings.</p>;
  } else if (id === triggerId) {
    body = <AutomationPanel file={file} edits={edits} />;
  } else if (job && id !== null) {
    body = <JobPanel key={id} id={id} file={file} graph={graph} nodeTypes={nodeTypes} edits={edits} />;
  } else {
    body = <p className="p-4 text-sm text-muted">There is no job {id} any more.</p>;
  }

  return (
    // Esc anywhere in the panel closes it; the aside is the region the key belongs to, not a control.
    // oxlint-disable-next-line jsx-a11y/no-noninteractive-element-interactions -- see above
    <aside
      aria-label={`Settings of ${title}`}
      onKeyDown={(event) => {
        if (event.key === 'Escape' && !event.defaultPrevented) {
          event.preventDefault();
          onClose();
        }
      }}
      className={clsx(
        'flex h-full max-w-full flex-col border-l border-line bg-surface',
        wide ? 'w-[40rem]' : 'w-[25rem]',
      )}
    >
      <header className="flex shrink-0 items-center gap-2 border-b border-line px-3 py-2">
        <div className="flex min-w-0 flex-1 flex-col">
          <h2 ref={headingRef} tabIndex={-1} className="truncate text-sm font-semibold outline-none">
            {title}
          </h2>
          {kind && <span className="text-xs text-muted">{kind}</span>}
        </div>
        <HeaderButton label={wide ? 'Narrow the panel' : 'Widen the panel'} onClick={() => setWide(!wide)}>
          {wide ? <Minimize2 aria-hidden="true" size={14} /> : <Maximize2 aria-hidden="true" size={14} />}
        </HeaderButton>
        {edits && job && id !== null && (
          <HeaderButton label={`Delete ${job.title}`} danger onClick={() => edits.remove(id)}>
            <Trash2 aria-hidden="true" size={14} />
          </HeaderButton>
        )}
        <HeaderButton label="Close the panel (Esc)" onClick={onClose}>
          <X aria-hidden="true" size={14} />
        </HeaderButton>
      </header>
      <div className="min-h-0 flex-1 overflow-y-auto">
        <PanelContext value={settings}>{body}</PanelContext>
      </div>
    </aside>
  );
}

function HeaderButton({
  label,
  onClick,
  danger = false,
  children,
}: {
  label: string;
  onClick: () => void;
  danger?: boolean;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      onClick={onClick}
      className={clsx(
        'rounded-control p-1.5 text-muted',
        danger ? 'hover:bg-danger-soft hover:text-danger' : 'hover:bg-sunken hover:text-ink',
      )}
    >
      {children}
    </button>
  );
}
