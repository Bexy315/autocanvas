import { useMemo, useState } from 'react';
import type { Diagnostic } from '../api/problem';
import { useSettled } from '../editor/useValidation';
import { jobLines, type LineRange } from './lines';
import { graphFromYaml, type Graph } from './model';

/** How long typing pauses before the canvas follows; short, as reading the file is cheap. */
export const followDelay = 150;

const emptyGraph: Graph = { triggers: [], jobs: [], edges: [], positions: {} };

/**
 * The canvas's view of the text being edited. While the text cannot be read (half-typed YAML),
 * the last readable graph stays, marked as stale, so the canvas does not flicker away.
 */
export function useCanvasGraph(text: string, diagnostics: readonly Diagnostic[]) {
  const settled = useSettled(text, followDelay);
  const read = useMemo(() => graphFromYaml(settled), [settled]);
  const lines = useMemo<ReadonlyMap<string, LineRange>>(() => jobLines(settled), [settled]);
  const [lastReadable, setLastReadable] = useState<Graph>(() => graphFromYaml(text).graph ?? emptyGraph);
  if (read.graph && read.graph !== lastReadable) {
    setLastReadable(read.graph);
  }

  const problemsByJob = useMemo(() => {
    const counts: Record<string, number> = {};
    for (const diagnostic of diagnostics) {
      const id = /^jobs\.([^.[]+)/.exec(diagnostic.path)?.[1];
      if (id) {
        counts[id] = (counts[id] ?? 0) + 1;
      }
    }
    return counts;
  }, [diagnostics]);

  return {
    graph: read.graph ?? lastReadable,
    stale: read.error !== undefined,
    error: read.error,
    lines,
    problemsByJob,
  };
}
