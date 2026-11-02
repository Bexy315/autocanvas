/**
 * The side panel for one job. A rail at the top lists what the job is made of, its own settings
 * first and then its steps in order; the rest of the panel belongs to what the rail has chosen
 * (harvested from the origin's JobConfigPanel). A switch or a join has no steps, so the whole panel
 * is its settings.
 */
import { clsx } from 'clsx';
import { ArrowDown, ArrowUp, Plus, Settings2, SquareTerminal, Trash2, Workflow } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import type { Graph } from '../model';
import { addStep, moveStep, removeStep, type NewStepKind, type NodeTypeView } from '../operations';
import { isMapping, positionKey, problemsUnder, type PanelFile } from '../panelModel';
import { YamlOnly } from './fields';
import { GeneralSection, NeedsSection, StepJobSections, SwitchSections } from './JobSettings';
import { setter, usePanel, type PanelEdits } from './panelContext';
import { StepEditor } from './StepEditor';

export function JobPanel({
  id,
  file,
  graph,
  nodeTypes,
  edits,
}: {
  id: string;
  file: PanelFile;
  graph: Graph;
  nodeTypes: readonly NodeTypeView[];
  edits: PanelEdits | undefined;
}) {
  const { diagnostics, readOnly } = usePanel();
  const [chosen, setChosen] = useState<number | null>(null);
  const job = graph.jobs.find((entry) => entry.id === id);
  const base = ['jobs', id];
  const steps = file.get([...base, 'steps']);
  const stepList: readonly unknown[] = Array.isArray(steps) ? steps : [];
  const onSet = setter(edits);
  // A step that is gone (deleted, or undone) leaves the job's settings chosen.
  const step = chosen !== null && chosen < stepList.length ? chosen : null;

  if (!job || !isMapping(file.get(base))) {
    return (
      <div className="p-4">
        <YamlOnly label={id} source={file.source(base)} />
      </div>
    );
  }
  const settingsProps = { id, file, graph, edits, onSet };
  if (job.kind !== 'step') {
    return (
      <>
        <GeneralSection {...settingsProps} />
        <NeedsSection {...settingsProps} />
        {job.kind === 'switch' && <SwitchSections {...settingsProps} />}
      </>
    );
  }

  const structural = (change: ReturnType<typeof moveStep>, next: number | null) => {
    edits?.apply(change, { step: true, announce: true });
    setChosen(next);
  };

  return (
    <>
      <ul aria-label={`Parts of ${job.title}`} className="max-h-60 shrink-0 overflow-y-auto border-b border-line">
        <li className="flex">
          <RailRow
            current={step === null}
            onChoose={() => setChosen(null)}
            icon={<Settings2 aria-hidden="true" size={14} />}
            title="Job settings"
            subtitle={[job.runsOn, job.image].filter(Boolean).join(' · ')}
            problems={problemsUnder(diagnostics, base) - problemsUnder(diagnostics, [...base, 'steps'])}
          />
        </li>
        {stepList.map((entry, index) => {
          const summary = job.steps[index];
          const title = isMapping(entry) && typeof entry['name'] === 'string' ? entry['name'] : summary?.label;
          return (
            <li key={positionKey(index)} className="group/row flex items-center">
              <RailRow
                current={step === index}
                onChoose={() => setChosen(index)}
                number={index + 1}
                icon={
                  summary?.nodeType ? (
                    <Workflow aria-hidden="true" size={14} />
                  ) : (
                    <SquareTerminal aria-hidden="true" size={14} />
                  )
                }
                title={title ?? `Step ${index + 1}`}
                subtitle={summary?.nodeType ?? ''}
                problems={problemsUnder(diagnostics, [...base, 'steps', index])}
              />
              {!readOnly && (
                <div className="flex shrink-0 items-center pr-2">
                  <IconButton
                    label={`Move step ${index + 1} up`}
                    disabled={index === 0}
                    onClick={() => structural(moveStep(edits?.current() ?? '', id, index, index - 1), index - 1)}
                  >
                    <ArrowUp aria-hidden="true" size={13} />
                  </IconButton>
                  <IconButton
                    label={`Move step ${index + 1} down`}
                    disabled={index === stepList.length - 1}
                    onClick={() => structural(moveStep(edits?.current() ?? '', id, index, index + 1), index + 1)}
                  >
                    <ArrowDown aria-hidden="true" size={13} />
                  </IconButton>
                  <IconButton
                    label={`Delete step ${index + 1}`}
                    danger
                    onClick={() => structural(removeStep(edits?.current() ?? '', id, index), null)}
                  >
                    <Trash2 aria-hidden="true" size={13} />
                  </IconButton>
                </div>
              )}
            </li>
          );
        })}
        {!readOnly && (
          <li className="px-2 py-1.5">
            <AddStep
              nodeTypes={nodeTypes}
              onAdd={(kind: NewStepKind) => {
                edits?.apply(addStep(edits.current(), id, kind), { step: true, announce: true });
                setChosen(stepList.length);
              }}
            />
          </li>
        )}
      </ul>
      {step === null ? (
        <>
          <GeneralSection {...settingsProps} />
          <NeedsSection {...settingsProps} />
          <StepJobSections {...settingsProps} />
        </>
      ) : (
        <StepEditor
          key={step}
          file={file}
          path={[...base, 'steps', step]}
          index={step}
          nodeTypes={nodeTypes}
          onSet={onSet}
        />
      )}
    </>
  );
}

function RailRow({
  current,
  onChoose,
  icon,
  title,
  subtitle,
  number,
  problems,
}: {
  current: boolean;
  onChoose: () => void;
  icon: ReactNode;
  title: string;
  subtitle: string;
  number?: number;
  problems: number;
}) {
  return (
    <button
      type="button"
      aria-current={current}
      onClick={onChoose}
      className={clsx(
        'flex min-w-0 flex-1 items-center gap-2 px-3 py-1.5 text-left text-sm',
        current ? 'bg-accent-soft text-accent' : 'hover:bg-sunken',
      )}
    >
      {number !== undefined && <span className="w-4 shrink-0 text-right text-xs text-muted">{number}</span>}
      <span className="shrink-0 text-muted">{icon}</span>
      <span className="min-w-0 flex-1">
        <span className="block truncate font-medium">{title}</span>
        {subtitle && <span className="block truncate font-mono text-xs text-muted">{subtitle}</span>}
      </span>
      {problems > 0 && (
        <span className="shrink-0 text-xs text-danger">{problems === 1 ? '1 problem' : `${problems} problems`}</span>
      )}
    </button>
  );
}

function IconButton({
  label,
  onClick,
  disabled = false,
  danger = false,
  children,
}: {
  label: string;
  onClick: () => void;
  disabled?: boolean;
  danger?: boolean;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      disabled={disabled}
      onClick={onClick}
      className={clsx(
        'rounded-control p-1 text-muted disabled:opacity-30',
        danger ? 'hover:bg-danger-soft hover:text-danger' : 'hover:bg-sunken hover:text-ink',
      )}
    >
      {children}
    </button>
  );
}

/** Adds a script, or a step of one of the node types, at the end of the job. */
function AddStep({ nodeTypes, onAdd }: { nodeTypes: readonly NodeTypeView[]; onAdd: (kind: NewStepKind) => void }) {
  const [open, setOpen] = useState(false);
  const choose = (kind: NewStepKind) => {
    setOpen(false);
    onAdd(kind);
  };
  return (
    <div className="flex flex-col gap-1">
      <button
        type="button"
        aria-expanded={open}
        onClick={() => setOpen(!open)}
        className="flex w-fit items-center gap-1 rounded-control px-1.5 py-1 text-xs font-medium text-accent hover:bg-accent-soft"
      >
        <Plus aria-hidden="true" size={12} />
        Add step
      </button>
      {open && (
        <fieldset className="flex flex-wrap gap-1">
          <legend className="sr-only">Kind of step</legend>
          <KindButton label="Script" onClick={() => choose('script')} />
          {nodeTypes.map((nodeType) => (
            <KindButton
              key={nodeType.name}
              label={nodeType.name}
              title={nodeType.description}
              onClick={() => choose({ nodeType })}
            />
          ))}
        </fieldset>
      )}
    </div>
  );
}

function KindButton({ label, title, onClick }: { label: string; title?: string; onClick: () => void }) {
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      className="rounded-control border border-line-strong bg-surface px-2 py-1 font-mono text-xs hover:bg-sunken"
    >
      {label}
    </button>
  );
}
