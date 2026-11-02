import { BaseEdge, EdgeLabelRenderer, getSmoothStepPath, type EdgeProps } from '@xyflow/react';
import type { EdgeData } from './flow';

const colors: Record<string, string> = {
  success: 'var(--color-ok)',
  failure: 'var(--color-danger)',
  always: 'var(--color-muted)',
  case: 'var(--color-accent)',
  start: 'var(--color-line-strong)',
  finally: 'var(--color-muted)',
};

/**
 * A line from what a job waits for. Its color repeats the output it leaves from; a label says the
 * same for failure, always and switch cases, so color is never the only way to tell.
 */
export function AutomationEdge(props: EdgeProps) {
  const { id, sourceX, sourceY, targetX, targetY, sourcePosition, targetPosition, markerEnd } = props;
  const data = props.data as EdgeData | undefined;
  const [path, labelX, labelY] = getSmoothStepPath({
    sourceX,
    sourceY,
    targetX,
    targetY,
    sourcePosition,
    targetPosition,
    borderRadius: 10,
  });
  const color = colors[data?.kind ?? 'success'] ?? colors['success'];

  return (
    <>
      <BaseEdge id={id} path={path} {...(markerEnd ? { markerEnd } : {})} style={{ stroke: color, strokeWidth: 1.5 }} />
      {data?.label && (
        <EdgeLabelRenderer>
          <span
            className="nodrag nopan pointer-events-none absolute rounded-full border border-line bg-surface px-1.5 text-[10px] text-ink"
            style={{ transform: `translate(-50%, -50%) translate(${labelX}px, ${labelY}px)` }}
          >
            {data.label}
          </span>
        </EdgeLabelRenderer>
      )}
    </>
  );
}

/** The dashed line into a finally job: drawn to show it runs last, not a stored dependency. */
export function FinallyEdge(props: EdgeProps) {
  const { id, sourceX, sourceY, targetX, targetY, sourcePosition, targetPosition } = props;
  const [path] = getSmoothStepPath({
    sourceX,
    sourceY,
    targetX,
    targetY,
    sourcePosition,
    targetPosition,
    borderRadius: 10,
  });
  return (
    <BaseEdge id={id} path={path} style={{ stroke: colors['finally'], strokeWidth: 1.5, strokeDasharray: '5 4' }} />
  );
}
