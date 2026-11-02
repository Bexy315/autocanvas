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
 *
 * The canvas follows typing once it pauses, but a text the canvas made itself (`fromCanvas`) at
 * once: a card dropped somewhere must not jump back to where it was for the length of the pause.
 */
export function useCanvasGraph(text: string, diagnostics: readonly Diagnostic[], fromCanvas: string | null = null) {
  const settled = useSettled(text, followDelay);
  const source = text === fromCanvas ? text : settled;
  const read = useMemo(() => graphFromYaml(source), [source]);
  const lines = useMemo<ReadonlyMap<string, LineRange>>(() => jobLines(source), [source]);
  const [lastReadable, setLastReadable] = useState<{ graph: Graph; text: string }>(() => ({
    graph: graphFromYaml(text).graph ?? emptyGraph,
    text,
  }));
  if (read.graph && read.graph !== lastReadable.graph) {
    setLastReadable({ graph: read.graph, text: source });
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
    graph: read.graph ?? lastReadable.graph,
    /** The text the graph was read from: the side panel reads the same. */
    text: read.graph ? source : lastReadable.text,
    stale: read.error !== undefined,
    error: read.error,
    lines,
    problemsByJob,
  };
}
