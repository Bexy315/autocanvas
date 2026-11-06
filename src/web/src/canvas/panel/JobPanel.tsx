/**
 * The side panel for one job, in two tabs: **Job**, its own settings in sections that say what they
 * hold while folded, and **Steps**, the list of its steps, where choosing one opens it in the same
 * tab with a way back and to its neighbours. A switch or a join has no steps, so the whole panel is
 * its settings.
 */
import { clsx } from 'clsx';
import { ArrowDown, ArrowUp, ChevronDown, ChevronLeft, ChevronUp, Plus, SquareTerminal, Trash2 } from 'lucide-react';
import { createElement, useId, useState, type KeyboardEvent, type ReactNode } from 'react';
import type { Graph } from '../model';
import { addStep, moveStep, removeStep, type NewStepKind, type NodeTypeView } from '../operations';
import { nodeTypeIcon } from '../paletteEntries';
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
  const [tab, setTab] = useState<TabKey>('job');
  const [chosen, setChosen] = useState<number | null>(null);
  const tabIds = useId();
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
  const jobProblems = problemsUnder(diagnostics, base) - problemsUnder(diagnostics, [...base, 'steps']);
  const stepProblems = problemsUnder(diagnostics, [...base, 'steps']);

  return (
    <>
      <Tabs
        label={`Parts of ${job.title}`}
        ids={tabIds}
        tab={tab}
        onTab={setTab}
        tabs={[
          { key: 'job', title: 'Job', problems: jobProblems },
          { key: 'steps', title: `Steps (${stepList.length})`, problems: stepProblems },
        ]}
      />
      {tab === 'job' && (
        <div role="tabpanel" id={`${tabIds}-job`} aria-labelledby={`${tabIds}-job-tab`}>
          <GeneralSection {...settingsProps} />
          <NeedsSection {...settingsProps} />
          <StepJobSections {...settingsProps} />
        </div>
      )}
      {tab === 'steps' && (
        <div role="tabpanel" id={`${tabIds}-steps`} aria-labelledby={`${tabIds}-steps-tab`}>
          {step === null ? (
            <ul aria-label={`Steps of ${job.title}`} className="flex flex-col py-1">
              {stepList.length === 0 && <li className="px-4 py-2 text-sm text-muted">No steps yet.</li>}
              {stepList.map((entry, index) => {
                const summary = job.steps[index];
                const title = isMapping(entry) && typeof entry['name'] === 'string' ? entry['name'] : summary?.label;
                return (
                  <li key={positionKey(index)} className="group/row flex items-center">
                    <RailRow
                      current={false}
                      onChoose={() => setChosen(index)}
                      number={index + 1}
                      icon={
                        summary?.nodeType ? (
                          createElement(nodeTypeIcon(summary.nodeType), { 'aria-hidden': true, size: 14 })
                        ) : (
                          <SquareTerminal aria-hidden="true" size={14} />
                        )
                      }
                      title={title ?? `Step ${index + 1}`}
                      subtitle={!summary?.nodeType ? 'script' : summary.nodeType === title ? '' : summary.nodeType}
                      problems={problemsUnder(diagnostics, [...base, 'steps', index])}
                    />
                    {!readOnly && (
                      <div className="flex shrink-0 items-center pr-2">
                        <IconButton
                          label={`Move step ${index + 1} up`}
                          disabled={index === 0}
                          onClick={() => structural(moveStep(edits?.current() ?? '', id, index, index - 1), null)}
                        >
                          <ArrowUp aria-hidden="true" size={13} />
                        </IconButton>
                        <IconButton
                          label={`Move step ${index + 1} down`}
                          disabled={index === stepList.length - 1}
                          onClick={() => structural(moveStep(edits?.current() ?? '', id, index, index + 1), null)}
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
                <li className="px-3 py-1.5">
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
          ) : (
            <>
              <nav
                aria-label="Steps"
                className="flex items-center gap-1 border-b border-line bg-sunken px-2 py-1.5 text-xs"
              >
                <button
                  type="button"
                  onClick={() => setChosen(null)}
                  className="flex items-center gap-0.5 rounded-control px-1.5 py-1 font-medium text-accent hover:bg-accent-soft"
                >
                  <ChevronLeft aria-hidden="true" size={13} />
                  All steps
                </button>
                <span className="ml-auto text-muted">
                  Step {step + 1} of {stepList.length}
                </span>
                <IconButton label="Previous step" disabled={step === 0} onClick={() => setChosen(step - 1)}>
                  <ChevronUp aria-hidden="true" size={13} />
                </IconButton>
                <IconButton
                  label="Next step"
                  disabled={step === stepList.length - 1}
                  onClick={() => setChosen(step + 1)}
                >
                  <ChevronDown aria-hidden="true" size={13} />
                </IconButton>
              </nav>
              <StepEditor
                key={step}
                file={file}
                path={[...base, 'steps', step]}
                index={step}
                nodeTypes={nodeTypes}
                onSet={onSet}
              />
            </>
          )}
        </div>
      )}
    </>
  );
}

type TabKey = 'job' | 'steps';

/**
 * The job's own settings and its steps, as two tabs (the ARIA tabs pattern: the arrow keys move
 * between them). Each tab counts the problems in its part.
 */
function Tabs({
  label,
  ids,
  tab,
  onTab,
  tabs,
}: {
  label: string;
  ids: string;
  tab: TabKey;
  onTab: (tab: TabKey) => void;
  tabs: { key: TabKey; title: string; problems: number }[];
}) {
  const move = (event: KeyboardEvent<HTMLButtonElement>) => {
    if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') {
      return;
    }
    event.preventDefault();
    const at = tabs.findIndex((entry) => entry.key === tab);
    const next = tabs[(at + (event.key === 'ArrowRight' ? 1 : tabs.length - 1)) % tabs.length]?.key ?? tab;
    onTab(next);
    document.getElementById(`${ids}-${next}-tab`)?.focus();
  };
  return (
    <div
      role="tablist"
      aria-label={label}
      className="sticky top-0 z-10 flex shrink-0 gap-1 border-b border-line bg-surface px-3 pt-2"
    >
      {tabs.map((entry) => (
        <button
          key={entry.key}
          id={`${ids}-${entry.key}-tab`}
          type="button"
          role="tab"
          aria-selected={tab === entry.key}
          aria-controls={`${ids}-${entry.key}`}
          tabIndex={tab === entry.key ? 0 : -1}
          onClick={() => onTab(entry.key)}
          onKeyDown={move}
          className={clsx(
            '-mb-px flex items-center gap-1.5 border-b-2 px-2.5 pb-2 text-sm',
            tab === entry.key
              ? 'border-accent font-semibold text-accent'
              : 'border-transparent text-muted hover:text-ink',
          )}
        >
          {entry.title}
          {entry.problems > 0 && (
            <span className="rounded-full bg-danger-soft px-1.5 text-xs font-medium text-danger">{entry.problems}</span>
          )}
        </button>
      ))}
    </div>
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
