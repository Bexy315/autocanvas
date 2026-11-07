/**
 * An attempt's log as the log view shows it: chunks put in order (each once, however often it
 * arrived), lines numbered from 1, and grouped by the step that wrote them, with a group for every
 * step of the attempt, also one that has written nothing yet.
 */
import type { JobStep, LogChunk, LogLine } from '../../api/runs';
import { plainText } from './ansi';

export interface NumberedLine extends LogLine {
  /** Counts the attempt's lines from 1, as `#L12` links use. */
  number: number;
  stepIndex: number | null;
  /** The text without escape sequences, for search. */
  plain: string;
}

/** A group of lines written by one step (or, with no step, by the runner before and after them). */
export interface LogGroup {
  /** Tells groups apart: the step's index, or where the runner wrote outside a step. */
  key: string;
  stepIndex: number | null;
  title: string;
  lines: NumberedLine[];
  /** The step, for its status and duration; none for the runner's lines. */
  step: JobStep | null;
}

export type LogRow =
  | { kind: 'group'; group: LogGroup; open: boolean }
  | { kind: 'line'; line: NumberedLine; group: LogGroup }
  /** An open group without lines, which says why there are none. */
  | { kind: 'empty'; group: LogGroup };

/** Adds chunks to those known, keyed by sequence, so a chunk that arrives twice counts once. */
export function mergeChunks(
  known: ReadonlyMap<number, LogChunk>,
  incoming: readonly LogChunk[],
): Map<number, LogChunk> {
  const merged = new Map(known);
  for (const chunk of incoming) {
    merged.set(Number(chunk.sequence), chunk);
  }
  return merged;
}

/** The highest sequence known: where reading on continues. */
export function lastSequence(chunks: ReadonlyMap<number, LogChunk>): number | null {
  let last: number | null = null;
  for (const sequence of chunks.keys()) {
    last = last === null || sequence > last ? sequence : last;
  }
  return last;
}

export function numberLines(chunks: ReadonlyMap<number, LogChunk>): NumberedLine[] {
  const lines: NumberedLine[] = [];
  for (const sequence of [...chunks.keys()].toSorted((a, b) => a - b)) {
    const chunk = chunks.get(sequence);
    const stepIndex = chunk?.stepIndex === undefined || chunk.stepIndex === null ? null : Number(chunk.stepIndex);
    for (const line of chunk?.lines ?? []) {
      lines.push({ ...line, number: lines.length + 1, stepIndex, plain: plainText(line.text) });
    }
  }
  return lines;
}

function stepTitle(stepIndex: number | null, steps: readonly JobStep[], seenSteps: boolean): string {
  if (stepIndex === null) {
    return seenSteps ? 'Cleaning up' : 'Preparing';
  }
  const step = steps.find((candidate) => Number(candidate.index) === stepIndex);
  return `${stepIndex + 1}. ${step?.name ?? `Step ${stepIndex + 1}`}`;
}

const stepOf = (stepIndex: number | null, steps: readonly JobStep[]) =>
  stepIndex === null ? null : (steps.find((step) => Number(step.index) === stepIndex) ?? null);

/**
 * Lines grouped by the step that wrote them, in the order they were written, and the steps that
 * wrote nothing (yet) in their place: after the steps before them, before the runner cleans up.
 */
export function groupLines(lines: readonly NumberedLine[], steps: readonly JobStep[]): LogGroup[] {
  const groups: LogGroup[] = [];
  let seenSteps = false;
  for (const line of lines) {
    const current = groups.at(-1);
    if (current && current.stepIndex === line.stepIndex) {
      current.lines.push(line);
      continue;
    }
    seenSteps ||= line.stepIndex !== null;
    groups.push({
      key: line.stepIndex === null ? `runner-${groups.length}` : `step-${line.stepIndex}`,
      stepIndex: line.stepIndex,
      title: stepTitle(line.stepIndex, steps, seenSteps && line.stepIndex === null),
      lines: [line],
      step: stepOf(line.stepIndex, steps),
    });
  }
  for (const step of steps.toSorted((a, b) => Number(a.index) - Number(b.index))) {
    const index = Number(step.index);
    if (groups.some((group) => group.stepIndex === index)) {
      continue;
    }
    const before = groups.findIndex((group) =>
      group.stepIndex === null ? group.title === 'Cleaning up' : group.stepIndex > index,
    );
    const group = { key: `step-${index}`, stepIndex: index, title: stepTitle(index, steps, true), lines: [], step };
    groups.splice(before === -1 ? groups.length : before, 0, group);
  }
  return groups;
}

const openStatuses: ReadonlySet<JobStep['status']> = new Set(['Running', 'Failed', 'TimedOut', 'Errored']);

/**
 * Whether a group is open until someone opens or closes it: a step while it runs and after it
 * failed, so what went wrong is in view and what went well is out of the way. The runner's lines
 * are open while no step has run, so a job that fails before its steps shows why.
 */
export function openByDefault(group: LogGroup, groups: readonly LogGroup[]): boolean {
  if (group.step) {
    return openStatuses.has(group.step.status);
  }
  if (group.stepIndex !== null) {
    return true;
  }
  return !groups.some((other) => other.step && other.step.status !== 'Sent' && other.step.status !== 'Skipped');
}

/** The rows the view draws: each group's heading, then its lines while it is open. */
export function logRows(groups: readonly LogGroup[], isOpen: (group: LogGroup) => boolean): LogRow[] {
  const rows: LogRow[] = [];
  for (const group of groups) {
    const open = isOpen(group);
    rows.push({ kind: 'group', group, open });
    if (open && group.lines.length === 0) {
      rows.push({ kind: 'empty', group });
    } else if (open) {
      for (const line of group.lines) {
        rows.push({ kind: 'line', line, group });
      }
    }
  }
  return rows;
}

/** The numbers of the lines containing `query`, ignoring case; none for an empty query. */
export function searchLines(lines: readonly NumberedLine[], query: string): number[] {
  const needle = query.trim().toLowerCase();
  if (needle === '') {
    return [];
  }
  return lines.filter((line) => line.plain.toLowerCase().includes(needle)).map((line) => line.number);
}

/** The log as plain text, for downloading it. */
export function logText(lines: readonly NumberedLine[]): string {
  return lines.map((line) => `${line.time} ${line.source === 'Stderr' ? 'E' : ' '} ${line.plain}`).join('\n');
}
