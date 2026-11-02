/**
 * The cards on the canvas. A job card shows what the job is made of, in order, because that is the
 * question someone opening an automation has. Harvested from the origin's editor nodes, without
 * editing (S3 step 5b) and in AutoCanvas's design tokens.
 */
import { Handle, Position, type NodeProps } from '@xyflow/react';
import { clsx } from 'clsx';
import type { ReactNode } from 'react';
import { CircleAlert, GitBranch, Merge, Play, Plug, SquareTerminal, Zap } from 'lucide-react';
import type { JobNodeData, TriggerNodeData } from './flow';

/** The three ways a job can be waited for; colors match the lines they start. */
const outputs = [
  { id: 'success', color: 'var(--color-ok)', title: 'when this job succeeds' },
  { id: 'failure', color: 'var(--color-danger)', title: 'when this job fails' },
  { id: 'always', color: 'var(--color-muted)', title: 'either way' },
] as const;

function frame(selected: boolean | undefined, problems: number, extra?: string): string {
  return clsx(
    'rounded-panel border bg-surface text-ink shadow-sm',
    problems > 0 ? 'border-danger' : selected ? 'border-accent ring-2 ring-accent/30' : 'border-line-strong',
    extra,
  );
}

function Header({ icon, title, problems }: { icon: ReactNode; title: string; problems: number }) {
  return (
    <div className="flex items-center gap-2 border-b border-line bg-sunken/60 px-3 py-2">
      <span className="text-muted">{icon}</span>
      <span className="min-w-0 flex-1 truncate font-medium">{title}</span>
      {problems > 0 && (
        <span className="flex items-center gap-1 text-xs text-danger">
          <CircleAlert aria-hidden="true" size={12} />
          {problems}
        </span>
      )}
    </div>
  );
}

const target = <Handle type="target" position={Position.Top} isConnectable={false} className="canvas-handle" />;

export function TriggerNode({ data, selected }: NodeProps) {
  const { triggers } = data as TriggerNodeData;
  return (
    <div className={frame(selected, 0, 'w-65')}>
      <Header icon={<Zap aria-hidden="true" size={14} />} title="Trigger" problems={0} />
      <ul className="px-3 py-2 text-xs">
        {triggers.length === 0 ? (
          <li className="text-muted">Nothing starts it yet</li>
        ) : (
          triggers.map((line) => <li key={line}>{line}</li>)
        )}
      </ul>
      <Handle id="output" type="source" position={Position.Bottom} isConnectable={false} className="canvas-handle" />
    </div>
  );
}

export function JobNode({ data, selected }: NodeProps) {
  const { job, problemCount } = data as JobNodeData;
  const problems = problemCount + job.problems.length;
  return (
    <div className={frame(selected, problems, clsx('w-65', job.isFinally && 'border-dashed'))}>
      {target}
      <Header
        icon={job.isFinally ? <Play aria-hidden="true" size={14} /> : <SquareTerminal aria-hidden="true" size={14} />}
        title={job.title}
        problems={problems}
      />
      <ol className="flex flex-col py-1 text-xs">
        {job.steps.length === 0 && <li className="px-3 py-1 text-muted">No steps</li>}
        {job.steps.map((step, index) => (
          // Steps have no identity beyond their place in the job.
          // oxlint-disable-next-line react/no-array-index-key
          <li key={index} className="flex items-center gap-1.5 px-3 py-1">
            {step.nodeType ? (
              <Plug aria-hidden="true" size={12} className="shrink-0 text-accent" />
            ) : (
              <span aria-hidden="true" className="w-3 shrink-0 text-center font-mono text-muted">
                $
              </span>
            )}
            <span className="truncate">{step.label}</span>
          </li>
        ))}
      </ol>
      {job.hints.length > 0 && (
        <ul className="border-t border-line px-3 py-1.5 text-[11px] text-muted">
          {job.hints.map((hint) => (
            <li key={hint} className="truncate">
              {hint}
            </li>
          ))}
        </ul>
      )}
      {job.problems.map((problem) => (
        <p key={problem} className="border-t border-line bg-danger-soft px-3 py-1.5 text-[11px] text-danger">
          {problem}
        </p>
      ))}
      <div className="flex items-center justify-between gap-2 border-t border-line px-3 py-1.5 text-[11px] text-muted">
        <span className="truncate font-mono">{job.runsOn ?? 'any runner'}</span>
        {job.isFinally ? <span>runs last</span> : <span className="truncate">{job.image}</span>}
      </div>
      {!job.isFinally &&
        outputs.map((output, index) => (
          <Handle
            key={output.id}
            id={output.id}
            type="source"
            position={Position.Bottom}
            isConnectable={false}
            title={output.title}
            className="canvas-handle"
            style={{ left: `${30 + index * 20}%`, background: output.color }}
          />
        ))}
      {job.isFinally && (
        <Handle id="always" type="source" position={Position.Bottom} isConnectable={false} className="canvas-handle" />
      )}
    </div>
  );
}

export function SwitchNode({ data, selected }: NodeProps) {
  const { job, problemCount } = data as JobNodeData;
  const problems = problemCount + job.problems.length;
  return (
    <div className={frame(selected, problems, 'w-65')}>
      {target}
      <Header icon={<GitBranch aria-hidden="true" size={14} />} title={job.title} problems={problems} />
      <p className="truncate border-b border-line px-3 py-1.5 font-mono text-[11px] text-muted">{job.expression}</p>
      <div className="flex justify-around gap-1 px-2 pt-1.5 pb-3 text-[11px]">
        {job.cases.map((entry) => (
          <span key={entry.name} className="flex min-w-0 flex-col items-center">
            <span className="truncate font-medium">{entry.name}</span>
            <span className="truncate text-muted">{entry.label}</span>
          </span>
        ))}
      </div>
      {job.problems.map((problem) => (
        <p key={problem} className="border-t border-line bg-danger-soft px-3 py-1.5 text-[11px] text-danger">
          {problem}
        </p>
      ))}
      {job.cases.map((entry, index) => (
        <Handle
          key={entry.name}
          id={entry.name}
          type="source"
          position={Position.Bottom}
          isConnectable={false}
          title={`case ${entry.name}`}
          className="canvas-handle"
          style={{ left: `${((index + 0.5) / job.cases.length) * 100}%`, background: 'var(--color-accent)' }}
        />
      ))}
    </div>
  );
}

export function JoinNode({ data, selected }: NodeProps) {
  const { job, problemCount } = data as JobNodeData;
  const problems = problemCount + job.problems.length;
  return (
    <div className={frame(selected, problems, 'w-45')}>
      {target}
      <div className="flex items-center gap-2 px-3 py-2.5">
        <Merge aria-hidden="true" size={14} className="text-muted" />
        <span className="truncate font-medium">{job.title}</span>
        {problems > 0 && <CircleAlert aria-label={`${problems} problems`} size={12} className="text-danger" />}
      </div>
      <Handle
        id="success"
        type="source"
        position={Position.Bottom}
        isConnectable={false}
        className="canvas-handle"
        style={{ background: 'var(--color-ok)' }}
      />
    </div>
  );
}
