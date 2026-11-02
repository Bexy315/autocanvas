/**
 * Where each card goes. Positions saved in the file stay; cards without one are placed by dagre,
 * top to bottom, as a file reads: the trigger first, then what waits for it. Harvested from the
 * origin's autoLayout.
 */
import { graphlib, layout as runLayout } from '@dagrejs/dagre';
import { triggerId, type Graph, type JobData } from './model';

export const cardWidth = 260;
const joinWidth = 180;
const headerHeight = 52;
const rowHeight = 26;
const footerHeight = 34;

export type Positions = Record<string, { x: number; y: number }>;

/**
 * A card's height, guessed from its content: the layout runs before the cards are drawn, and
 * waiting for real sizes would make the canvas jump on every load.
 */
export function estimateHeight(job: JobData | undefined, triggerLines = 1): number {
  if (!job) {
    return 44 + Math.max(triggerLines, 1) * 20;
  }
  if (job.kind === 'join') {
    return 64;
  }
  if (job.kind === 'switch') {
    return headerHeight + Math.max(job.cases.length, 1) * rowHeight + footerHeight;
  }
  return headerHeight + Math.max(job.steps.length, 1) * rowHeight + job.hints.length * 18 + footerHeight;
}

export function widthOf(job: JobData | undefined): number {
  return job?.kind === 'join' ? joinWidth : cardWidth;
}

/** Space between the lowest card and a finally job moved below it. */
const finallyGap = 96;

/**
 * Positions for every card: saved ones as they are, missing ones from dagre, finally jobs last.
 * With `all`, every card is placed anew (Tidy up), overriding what was saved.
 */
export function layOut(graph: Graph, { all = false }: { all?: boolean } = {}): Positions {
  const ids = [triggerId, ...graph.jobs.map((job) => job.id)];
  const byId = new Map(graph.jobs.map((job) => [job.id, job]));
  const positions: Positions = all ? {} : { ...graph.positions };
  const missing = ids.filter((id) => !(id in positions));

  if (missing.length > 0) {
    const layout = new graphlib.Graph();
    layout.setGraph({ rankdir: 'TB', nodesep: 40, ranksep: 64 });
    layout.setDefaultEdgeLabel(() => ({}));
    for (const id of ids) {
      layout.setNode(id, { width: widthOf(byId.get(id)), height: estimateHeight(byId.get(id), graph.triggers.length) });
    }
    for (const edge of graph.edges) {
      layout.setEdge(edge.source, edge.target);
    }
    runLayout(layout);
    for (const id of missing) {
      const laid = layout.node(id) as { x: number; y: number } | undefined;
      if (laid) {
        // dagre gives centres; cards are placed by their top-left corner.
        positions[id] = {
          x: Math.round(laid.x - widthOf(byId.get(id)) / 2),
          y: Math.round(laid.y - estimateHeight(byId.get(id), graph.triggers.length) / 2),
        };
      }
    }
  }

  return keepFinallyLast(graph, positions);
}

/** A finally job runs last, so it is drawn below everything else, wherever it was saved. */
function keepFinallyLast(graph: Graph, positions: Positions): Positions {
  const finals = graph.jobs.filter((job) => job.isFinally);
  if (finals.length === 0) {
    return positions;
  }
  const others = [undefined, ...graph.jobs.filter((job) => !job.isFinally)];
  const bottom = Math.max(
    ...others.map((job) => (positions[job?.id ?? triggerId]?.y ?? 0) + estimateHeight(job, graph.triggers.length)),
  );
  const result = { ...positions };
  for (const job of finals) {
    const at = result[job.id];
    if (at && at.y < bottom + 24) {
      result[job.id] = { x: at.x, y: bottom + finallyGap };
    }
  }
  return result;
}

/**
 * The nearest place to `at` where a new card does not cover another: moved down in steps until it
 * is clear, so cards added in the middle of the view do not end up on top of each other.
 */
export function freeSpot(at: { x: number; y: number }, positions: Positions): { x: number; y: number } {
  const taken = Object.values(positions);
  const clear = (y: number) =>
    taken.every((other) => Math.abs(other.x - at.x) >= cardWidth || Math.abs(other.y - y) >= 140);
  let y = at.y;
  for (let step = 0; step < 40 && !clear(y); step += 1) {
    y += 40;
  }
  return { x: at.x, y };
}
