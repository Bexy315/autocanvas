import '@xyflow/react/dist/base.css';
import './canvas.css';
import {
  Background,
  BackgroundVariant,
  Controls,
  ReactFlow,
  ReactFlowProvider,
  useReactFlow,
  type Node,
} from '@xyflow/react';
import { clsx } from 'clsx';
import { useEffect, useMemo, useRef } from 'react';
import { toFlow } from './flow';
import { layOut } from './layout';
import { triggerId, type Graph } from './model';
import { edgeTypes, nodeTypes } from './types';

export interface AutomationCanvasProps {
  graph: Graph;
  /** The file cannot be read right now; the graph is the last readable one. */
  stale?: boolean;
  /** The job the YAML cursor is in, highlighted and brought into view. */
  selectedJob?: string | null;
  problemsByJob?: Readonly<Record<string, number>>;
  /** A card was chosen (click, or Enter/Space on a focused card). */
  onSelectJob?: (id: string) => void;
}

/** The automation as a graph to read (S3 step 5a); editing on the canvas comes with step 5b. */
export function AutomationCanvas(props: AutomationCanvasProps) {
  return (
    <ReactFlowProvider>
      <Canvas {...props} />
    </ReactFlowProvider>
  );
}

const noProblems: Readonly<Record<string, number>> = {};

/** Fitting keeps the cards readable: a large graph is fitted at this zoom and panned. */
const fitOptions = { padding: 0.15, maxZoom: 1, minZoom: 0.55 };

function Canvas({
  graph,
  stale = false,
  selectedJob = null,
  problemsByJob = noProblems,
  onSelectJob,
}: AutomationCanvasProps) {
  const flow = useReactFlow();
  const host = useRef<HTMLDivElement>(null);
  const positions = useMemo(() => layOut(graph), [graph]);
  const { nodes, edges } = useMemo(() => {
    const built = toFlow(graph, positions, problemsByJob);
    return {
      nodes: built.nodes.map((node): Node => ({ ...node, selected: node.id === selectedJob })),
      edges: built.edges,
    };
  }, [graph, positions, problemsByJob, selectedJob]);

  // A job chosen in the YAML is brought into view, unless it already is in the middle of it.
  useEffect(() => {
    const at = selectedJob ? positions[selectedJob] : undefined;
    if (at) {
      void flow.setCenter(at.x + 130, at.y + 60, { zoom: Math.max(flow.getZoom(), 0.8), duration: 250 });
    }
  }, [selectedJob, positions, flow]);

  // Shown, hidden, or resized with the split: the graph is fitted to the space it has.
  useEffect(() => {
    const element = host.current;
    if (!element || typeof ResizeObserver === 'undefined') {
      return undefined;
    }
    let width = element.clientWidth;
    const observer = new ResizeObserver(() => {
      if (element.clientWidth > 0 && Math.abs(element.clientWidth - width) > 40) {
        width = element.clientWidth;
        void flow.fitView({ ...fitOptions, duration: 200 });
      }
    });
    observer.observe(element);
    return () => observer.disconnect();
  }, [flow]);

  return (
    <div ref={host} className={clsx('automation-canvas h-full', stale && 'is-stale')}>
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        edgeTypes={edgeTypes}
        nodesDraggable={false}
        nodesConnectable={false}
        elementsSelectable
        nodesFocusable
        edgesFocusable={false}
        fitView
        fitViewOptions={fitOptions}
        minZoom={0.2}
        // Only what the user selects (click, or Enter on a focused card) is reported; the
        // selection set from the YAML cursor comes in through props and must not echo back.
        onNodesChange={(changes) => {
          const chosen = changes.find((change) => change.type === 'select' && change.selected);
          if (chosen && 'id' in chosen && chosen.id !== triggerId) {
            onSelectJob?.(chosen.id);
          }
        }}
        ariaLabelConfig={{ 'node.a11yDescription.default': 'Press Enter or Space to show it in the file.' }}
      >
        <Background variant={BackgroundVariant.Dots} gap={20} size={1} color="var(--color-grid)" />
        <Controls showInteractive={false} position="bottom-left" />
      </ReactFlow>
    </div>
  );
}
