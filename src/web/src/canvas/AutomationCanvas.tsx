import '@xyflow/react/dist/base.css';
import './canvas.css';
import {
  Background,
  BackgroundVariant,
  Controls,
  ReactFlow,
  ReactFlowProvider,
  useReactFlow,
  type Edge,
  type Node,
  type NodeChange,
  type EdgeChange,
} from '@xyflow/react';
import { clsx } from 'clsx';
import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { toFlow } from './flow';
import { freeSpot, layOut, type Positions } from './layout';
import { triggerId, type Graph } from './model';
import type { NewJobKind, NodeTypeView } from './operations';
import { Palette } from './Palette';
import { kindOfDrop, paletteMime } from './paletteEntries';
import { edgeTypes, nodeTypes } from './types';

interface Size {
  width: number;
  height: number;
}

/** A line as the canvas knows it: from an output of one job to another job. */
export interface Line {
  source: string;
  handle: string;
  target: string;
}

/** What an editor can do on the canvas; the canvas reports intents, the editor changes the file. */
export interface CanvasEdits {
  move: (positions: Positions) => void;
  add: (kind: NewJobKind, at: { x: number; y: number }) => void;
  connect: (line: Line) => void;
  /** Whether a line may be drawn; checked while dragging, so it must be quick. */
  canConnect: (source: string, target: string) => boolean;
  disconnect: (lines: Line[]) => void;
  remove: (ids: string[]) => void;
  undo: () => void;
  redo: () => void;
}

export interface AutomationCanvasProps {
  graph: Graph;
  /** The file cannot be read right now; the graph is the last readable one. */
  stale?: boolean;
  /** The job the YAML cursor is in, highlighted and brought into view. */
  selectedJob?: string | null;
  problemsByJob?: Readonly<Record<string, number>>;
  /** A card was chosen (click, or Enter/Space on a focused card). */
  onSelectJob?: (id: string) => void;
  /** For editors: changes made on the canvas. Without it the canvas only shows. */
  edits?: CanvasEdits;
  /** The node types the palette offers. */
  paletteNodeTypes?: readonly NodeTypeView[];
  /** Changes when the whole graph was laid out anew (Tidy up): the view is fitted to it again. */
  fitKey?: number;
}

/** The automation as a graph: to read, and for editors to change (S3 step 5b). */
export function AutomationCanvas(props: AutomationCanvasProps) {
  return (
    <ReactFlowProvider>
      <Canvas {...props} />
    </ReactFlowProvider>
  );
}

const noProblems: Readonly<Record<string, number>> = {};
const noNodeTypes: readonly NodeTypeView[] = [];

/** Fitting keeps the cards readable: a large graph is fitted at this zoom and panned. */
const fitOptions = { padding: 0.15, maxZoom: 1, minZoom: 0.55 };

interface Selection {
  nodes: string[];
  edges: string[];
  /** The job the YAML cursor was in when this selection was made; a new one starts a new selection. */
  from: string | null;
}

function lineOf(edge: Edge): Line {
  return { source: edge.source, handle: edge.sourceHandle ?? 'success', target: edge.target };
}

function Canvas({
  graph,
  stale = false,
  selectedJob = null,
  problemsByJob = noProblems,
  onSelectJob,
  edits,
  paletteNodeTypes = noNodeTypes,
  fitKey = 0,
}: AutomationCanvasProps) {
  const flow = useReactFlow();
  const host = useRef<HTMLDivElement>(null);
  const editable = edits !== undefined && !stale;
  // Where cards are while being dragged; saved to the file when the drag ends.
  const [dragged, setDragged] = useState<Positions>({});
  const [selection, setSelection] = useState<Selection>({ nodes: [], edges: [], from: null });
  // The size React Flow measured for each card. Cards are built anew whenever something changes;
  // without their size, React Flow hides them (and their lines) until it has measured them again,
  // which made the canvas flicker on every step of a drag.
  const [sizes, setSizes] = useState<Readonly<Record<string, Size>>>({});

  // The cursor moved to another job in the YAML: that job becomes the selection.
  if (selection.from !== selectedJob) {
    setSelection({ nodes: selectedJob ? [selectedJob] : [], edges: [], from: selectedJob });
  }

  const positions = useMemo(() => layOut(graph), [graph]);
  const { nodes, edges } = useMemo(() => {
    const built = toFlow(graph, { ...positions, ...dragged }, problemsByJob, editable);
    return {
      // toFlow builds new objects every time, so they can take the selection directly.
      nodes: built.nodes.map((node): Node =>
        Object.assign(node, {
          selected: selection.nodes.includes(node.id),
          ...(sizes[node.id] ? { measured: sizes[node.id] } : {}),
        }),
      ),
      edges: built.edges.map((edge): Edge => Object.assign(edge, { selected: selection.edges.includes(edge.id) })),
    };
  }, [graph, positions, dragged, problemsByJob, editable, selection, sizes]);

  // A job chosen in the YAML is brought into view once, when it becomes the chosen one; later
  // changes to the graph (moves, Tidy up) leave the view where the user has it.
  const centered = useRef<string | null>(null);
  useEffect(() => {
    if (selectedJob === centered.current) {
      return;
    }
    const at = selectedJob ? positions[selectedJob] : undefined;
    if (at) {
      centered.current = selectedJob;
      void flow.setCenter(at.x + 130, at.y + 60, { zoom: Math.max(flow.getZoom(), 0.8), duration: 250 });
    } else if (!selectedJob) {
      centered.current = null;
    }
  }, [selectedJob, positions, flow]);

  // After Tidy up the cards are somewhere else; the view goes with them once they are drawn.
  useEffect(() => {
    if (fitKey > 0) {
      const timer = setTimeout(() => void flow.fitView({ ...fitOptions, duration: 250 }), 50);
      return () => clearTimeout(timer);
    }
    return undefined;
  }, [fitKey, flow]);

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

  const changeNodes = (changes: NodeChange[]) => {
    // Only what the user selects is reported; the selection from the YAML cursor comes in through
    // props and must not echo back.
    // Only cards that exist: the cursor may have been in a job that is gone by now.
    const shown = new Set([triggerId, ...graph.jobs.map((job) => job.id)]);
    let nodesSelected = selection.nodes.filter((id) => shown.has(id));
    let picked: string | null = null;
    const next: Positions = { ...dragged };
    let moved = false;
    let settled = false;
    let measured: Record<string, Size> | null = null;
    for (const change of changes) {
      if (change.type === 'select') {
        nodesSelected = change.selected
          ? [...nodesSelected.filter((id) => id !== change.id), change.id]
          : nodesSelected.filter((id) => id !== change.id);
        picked = change.selected && change.id !== triggerId ? change.id : picked;
      } else if (change.type === 'position' && change.position && editable) {
        next[change.id] = change.position;
        moved = true;
        // A mouse drag ends with `dragging: false`; a move with the arrow keys has no drag at all.
        settled ||= change.dragging !== true;
      } else if (change.type === 'dimensions' && change.dimensions) {
        const { width, height } = change.dimensions;
        if (sizes[change.id]?.width !== width || sizes[change.id]?.height !== height) {
          measured = { ...(measured ?? sizes), [change.id]: { width, height } };
        }
      }
    }
    if (measured) {
      setSizes(measured);
    }
    if (nodesSelected.join() !== selection.nodes.join()) {
      setSelection({ ...selection, nodes: nodesSelected });
    }
    if (moved) {
      if (settled) {
        edits?.move(next);
        setDragged({});
      } else {
        setDragged(next);
      }
    }
    if (picked && nodesSelected.length === 1) {
      onSelectJob?.(picked);
    }
  };

  const changeEdges = (changes: EdgeChange[]) => {
    let edgesSelected = selection.edges;
    for (const change of changes) {
      if (change.type === 'select') {
        edgesSelected = change.selected
          ? [...edgesSelected.filter((id) => id !== change.id), change.id]
          : edgesSelected.filter((id) => id !== change.id);
      }
    }
    if (edgesSelected !== selection.edges) {
      setSelection({ ...selection, edges: edgesSelected });
    }
  };

  const addInMiddle = (kind: NewJobKind) => {
    const box = host.current?.getBoundingClientRect();
    const at = flow.screenToFlowPosition({
      x: (box?.left ?? 0) + (box?.width ?? 0) / 2,
      y: (box?.top ?? 0) + (box?.height ?? 0) / 2,
    });
    edits?.add(kind, freeSpot({ x: at.x - 130, y: at.y - 40 }, positions));
  };

  const keys = (event: KeyboardEvent<HTMLDivElement>) => {
    if (!editable || !(event.ctrlKey || event.metaKey)) {
      return;
    }
    const key = event.key.toLowerCase();
    if (key === 'z' && !event.shiftKey) {
      event.preventDefault();
      edits?.undo();
    } else if (key === 'y' || (key === 'z' && event.shiftKey)) {
      event.preventDefault();
      edits?.redo();
    }
  };

  return (
    <div className="flex h-full flex-col">
      {editable && <Palette nodeTypes={paletteNodeTypes} onAdd={addInMiddle} />}
      {/* Undo and redo while the canvas has the focus; the cards inside are what is focused, so
          the container itself is not a tab stop. */}
      {/* oxlint-disable-next-line jsx-a11y/no-static-element-interactions */}
      <div
        ref={host}
        className={clsx('automation-canvas relative min-h-0 flex-1', stale && 'is-stale')}
        onKeyDown={keys}
        onDragOver={(event) => {
          if (editable && event.dataTransfer.types.includes(paletteMime)) {
            event.preventDefault();
            event.dataTransfer.dropEffect = 'copy';
          }
        }}
        onDrop={(event) => {
          const kind = editable ? kindOfDrop(event, paletteNodeTypes) : null;
          if (kind) {
            event.preventDefault();
            const at = flow.screenToFlowPosition({ x: event.clientX, y: event.clientY });
            edits?.add(kind, freeSpot({ x: at.x - 130, y: at.y - 20 }, positions));
          }
        }}
      >
        <ReactFlow
          nodes={nodes}
          edges={edges}
          nodeTypes={nodeTypes}
          edgeTypes={edgeTypes}
          nodesDraggable={editable}
          nodesConnectable={editable}
          elementsSelectable
          nodesFocusable
          edgesFocusable={editable}
          selectionKeyCode="Shift"
          multiSelectionKeyCode={['Control', 'Meta', 'Shift']}
          deleteKeyCode={editable ? ['Backspace', 'Delete'] : null}
          fitView
          fitViewOptions={fitOptions}
          minZoom={0.2}
          onNodesChange={changeNodes}
          onEdgesChange={changeEdges}
          onNodesDelete={(deleted) => edits?.remove(deleted.map((node) => node.id).filter((id) => id !== triggerId))}
          onEdgesDelete={(deleted) => edits?.disconnect(deleted.map(lineOf))}
          onConnect={(connection) =>
            edits?.connect({
              source: connection.source,
              handle: connection.sourceHandle ?? 'success',
              target: connection.target,
            })
          }
          isValidConnection={(connection) =>
            connection.source !== connection.target &&
            (edits?.canConnect(connection.source, connection.target) ?? false)
          }
          ariaLabelConfig={{
            'node.a11yDescription.default': editable
              ? 'Press Enter or Space to show it in the file, the arrow keys to move it, Delete to delete it.'
              : 'Press Enter or Space to show it in the file.',
          }}
        >
          <Background variant={BackgroundVariant.Dots} gap={20} size={1} color="var(--color-grid)" />
          <Controls showInteractive={false} position="bottom-left" />
        </ReactFlow>
      </div>
    </div>
  );
}
