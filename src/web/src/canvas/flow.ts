import type { Edge, Node } from '@xyflow/react';
import type { Positions } from './layout';
import { triggerId, type EdgeKind, type Graph, type JobData } from './model';

export interface TriggerNodeData extends Record<string, unknown> {
  triggers: string[];
}

export interface JobNodeData extends Record<string, unknown> {
  job: JobData;
  /** Problems the server found in this job's part of the file. */
  problemCount: number;
}

export interface EdgeData extends Record<string, unknown> {
  kind: EdgeKind;
  label?: string;
}

const nodeType = { step: 'job', switch: 'switch', join: 'join' } as const;

function describe(job: JobData, needs: readonly string[], problemCount: number): string {
  const kind = job.kind === 'switch' ? 'Switch' : job.kind === 'join' ? 'Join' : job.isFinally ? 'Finally job' : 'Job';
  const parts = [`${kind} ${job.title}`];
  if (job.kind === 'step') {
    parts.push(job.steps.length === 1 ? '1 step' : `${job.steps.length} steps`);
  }
  if (job.kind === 'switch') {
    parts.push(`cases ${job.cases.map((entry) => entry.name).join(', ')}`);
  }
  parts.push(needs.length > 0 ? `needs ${needs.join(', ')}` : job.isFinally ? 'runs last' : 'starts with the run');
  const problems = problemCount + job.problems.length;
  if (problems > 0) {
    parts.push(problems === 1 ? '1 problem' : `${problems} problems`);
  }
  return parts.join(', ');
}

/**
 * The graph as React Flow nodes and edges, in reading order (top to bottom, left to right), so
 * Tab moves through the cards the way the automation runs.
 */
export function toFlow(
  graph: Graph,
  positions: Positions,
  problemsByJob: Readonly<Record<string, number>> = {},
): { nodes: Node[]; edges: Edge[] } {
  const needs = (id: string) =>
    graph.edges
      .filter((edge) => edge.target === id && edge.source !== triggerId && edge.kind !== 'finally')
      .map((edge) => edge.source);

  const nodes: Node[] = [
    {
      id: triggerId,
      type: 'trigger',
      position: positions[triggerId] ?? { x: 0, y: 0 },
      data: { triggers: graph.triggers } satisfies TriggerNodeData,
      ariaLabel: `Trigger: ${graph.triggers.length > 0 ? graph.triggers.join(', ') : 'none'}`,
    },
    ...graph.jobs.map((job): Node => {
      const problemCount = problemsByJob[job.id] ?? 0;
      return {
        id: job.id,
        type: nodeType[job.kind],
        position: positions[job.id] ?? { x: 0, y: 0 },
        data: { job, problemCount } satisfies JobNodeData,
        ariaLabel: describe(job, needs(job.id), problemCount),
      };
    }),
  ].toSorted((a, b) => a.position.y - b.position.y || a.position.x - b.position.x);

  const edges: Edge[] = graph.edges.map((edge) => ({
    id: edge.id,
    source: edge.source,
    target: edge.target,
    sourceHandle: edge.sourceHandle,
    type: edge.kind === 'finally' ? 'finally' : 'automation',
    data: { kind: edge.kind, ...(edge.label ? { label: edge.label } : {}) } satisfies EdgeData,
    focusable: false,
    selectable: false,
  }));

  return { nodes, edges };
}
