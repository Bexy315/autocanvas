import type { EdgeTypes, NodeTypes } from '@xyflow/react';
import { AutomationEdge, FinallyEdge } from './edges';
import { JobNode, JoinNode, SwitchNode, TriggerNode } from './nodes';

/** Card components by node type; defined once, as React Flow asks. */
export const nodeTypes: NodeTypes = { trigger: TriggerNode, job: JobNode, switch: SwitchNode, join: JoinNode };

export const edgeTypes: EdgeTypes = { automation: AutomationEdge, finally: FinallyEdge };
