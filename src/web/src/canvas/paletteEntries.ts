import { Container, GitBranch, Globe, Merge, Play, Plug, SquareTerminal, type LucideIcon } from 'lucide-react';
import type { DragEvent } from 'react';
import type { NewJobKind, NodeTypeView } from './operations';

/** The drag-and-drop type of a palette entry, so drops from elsewhere are ignored. */
export const paletteMime = 'application/x-autocanvas-job';

export interface Entry {
  key: string;
  label: string;
  description: string;
  Icon: LucideIcon;
  kind: NewJobKind;
}

export const flowEntries: Entry[] = [
  { key: 'job', label: 'Job', description: 'Runs steps on a runner', Icon: SquareTerminal, kind: 'job' },
  { key: 'switch', label: 'Switch', description: 'Picks one way by a value', Icon: GitBranch, kind: 'switch' },
  { key: 'join', label: 'Join', description: 'Waits for several, then goes on', Icon: Merge, kind: 'join' },
  { key: 'finally', label: 'Finally', description: 'Runs last, whatever happened', Icon: Play, kind: 'finally' },
];

const nodeTypeIcons: Readonly<Record<string, LucideIcon>> = { docker_build: Container, http_request: Globe };

/** A node type's icon: its own for the built-in ones, a plug for the others (plugins). */
export function nodeTypeIcon(name: string): LucideIcon {
  return nodeTypeIcons[name] ?? Plug;
}

/** Reads the kind a palette drag carries; null for anything else. */
export function kindOfDrop(event: DragEvent, nodeTypes: readonly NodeTypeView[]): NewJobKind | null {
  const key = event.dataTransfer.getData(paletteMime);
  const flow = flowEntries.find((entry) => entry.key === key);
  if (flow) {
    return flow.kind;
  }
  const nodeType = nodeTypes.find((type) => `node:${type.name}` === key);
  return nodeType ? { nodeType } : null;
}
