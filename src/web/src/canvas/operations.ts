/**
 * What the canvas does to the file. Each operation takes the current text and returns the new one,
 * changed only where it must be (yamlEdit), so the YAML stays the single source of truth and a
 * canvas change is one step in the editor's undo history like typing is.
 */
import { parseDocument } from 'yaml';
import type { components } from '../api/schema';
import { layOut, type Positions } from './layout';
import { graphFromYaml, triggerId } from './model';
import { appendItem, deleteIn, removeItem, setIn, type EditResult } from './yamlEdit';

export type NodeTypeView = components['schemas']['NodeTypeView'];

/** What the palette adds: the flow-control jobs, or a job with one step of a node type. */
export type NewJobKind = 'job' | 'switch' | 'join' | 'finally' | { nodeType: NodeTypeView };

export interface Change {
  text: string;
  /** What happened, in words, for the live region and tests. */
  summary: string;
  /** The job the change is about, to select it on the canvas. */
  focus?: string;
  /**
   * Whether a splice failed its check and the whole file was written anew (yamlEdit). The content
   * is right either way; the formatting of untouched parts may have changed.
   */
  rewritten?: boolean;
}

type Mapping = Record<string, unknown>;

function jobsOf(text: string): Mapping {
  const data: unknown = parseDocument(text).toJS();
  const jobs = typeof data === 'object' && data !== null ? (data as Mapping)['jobs'] : undefined;
  return typeof jobs === 'object' && jobs !== null && !Array.isArray(jobs) ? (jobs as Mapping) : {};
}

function needsOf(job: unknown): unknown[] {
  const needs = typeof job === 'object' && job !== null ? (job as Mapping)['needs'] : undefined;
  return Array.isArray(needs) ? needs : [];
}

/** An id no job holds yet: `job`, then `job2`, `job3`, … (harvested from the origin's newJobId). */
export function newJobId(existing: Readonly<Mapping>, base: string): string {
  if (!(base in existing)) {
    return base;
  }
  let number = 2;
  while (`${base}${number}` in existing) {
    number += 1;
  }
  return `${base}${number}`;
}

const runner = { runs_on: 'linux', image: 'alpine:3.22' };

/** The job a palette entry adds; a node type's required inputs are left empty for the panel to fill. */
function template(kind: NewJobKind): { base: string; job: Mapping } {
  if (typeof kind === 'object') {
    const required = kind.nodeType.inputs.filter((input) => input.required);
    const step: Mapping = { type: kind.nodeType.name };
    if (required.length > 0) {
      step['with'] = Object.fromEntries(required.map((input) => [input.name, '']));
    }
    return { base: kind.nodeType.name.replaceAll('_', '-'), job: { ...runner, steps: [step] } };
  }
  switch (kind) {
    case 'switch':
      return {
        base: 'switch',
        job: {
          type: 'switch',
          expression: '${{ trigger.event }}',
          cases: [
            { name: 'manual', when: 'manual' },
            { name: 'other', default: true },
          ],
        },
      };
    case 'join':
      return { base: 'join', job: { type: 'join' } };
    case 'finally':
      return { base: 'finally', job: { finally: true, ...runner, steps: [{ run: 'echo "The run is done"' }] } };
    default:
      return { base: 'job', job: { ...runner, steps: [{ run: 'echo "Hello"' }] } };
  }
}

/** Whole pixels, and never `-0`, which would be written as such. */
const rounded = ({ x, y }: { x: number; y: number }) => ({ x: Math.round(x) || 0, y: Math.round(y) || 0 });

/** Runs edits one after another, noting whether any had to write the whole file. */
class Edits {
  private rewritten = false;
  private text: string;

  constructor(text: string) {
    this.text = text;
  }

  run(edit: (current: string) => EditResult): this {
    const result = edit(this.text);
    this.text = result.text;
    this.rewritten ||= result.fallback;
    return this;
  }

  done(summary: string, focus?: string): Change {
    return {
      text: this.text,
      summary,
      ...(focus === undefined ? {} : { focus }),
      ...(this.rewritten ? { rewritten: true } : {}),
    };
  }
}

/** Saves where cards are, one `x-editor.layout` entry each. */
export function movePositions(text: string, positions: Positions): Change {
  const ids = Object.keys(positions);
  const edits = new Edits(text);
  for (const id of ids) {
    const at = rounded(positions[id] ?? { x: 0, y: 0 });
    edits.run((current) => setIn(current, ['x-editor', 'layout', id], at, { flow: true }));
  }
  return edits.done(ids.length === 1 ? `Moved ${ids[0]}` : `Moved ${ids.length} cards`);
}

/** Adds a job from the palette at a place on the canvas. */
export function addJob(text: string, kind: NewJobKind, at: { x: number; y: number }): Change {
  const { base, job } = template(kind);
  const id = newJobId(jobsOf(text), base);
  return new Edits(text)
    .run((current) => setIn(current, ['jobs', id], job))
    .run((current) => setIn(current, ['x-editor', 'layout', id], rounded(at), { flow: true }))
    .done(`Added ${typeof kind === 'object' ? kind.nodeType.name : kind} ${id}`, id);
}

/** How a line from `handle` of `source` is written in `needs`. */
function need(source: string, handle: string, sourceIsSwitch: boolean): unknown {
  if (sourceIsSwitch) {
    return { job: source, case: handle };
  }
  return handle === 'failure' || handle === 'always' ? { job: source, on: handle } : source;
}

/** A need in one form: `build` and `{ job: build, on: success }` mean the same. */
function normalNeed(value: unknown): string {
  return typeof value === 'string'
    ? JSON.stringify({ job: value, on: 'success' })
    : JSON.stringify({ on: 'success', ...(value as Mapping) });
}

function sameNeed(a: unknown, b: unknown): boolean {
  return normalNeed(a) === normalNeed(b);
}

/** Whether a line may be drawn from `source` to `target`; the reason when not. */
export function connectionProblem(text: string, source: string, target: string): string | null {
  if (source === target) {
    return 'A job cannot wait for itself.';
  }
  if (source === triggerId || target === triggerId) {
    return source === triggerId ? 'Jobs without needs start with the run already.' : 'Nothing runs before the trigger.';
  }
  const jobs = jobsOf(text);
  const targetJob = jobs[target] as Mapping | undefined;
  if (targetJob?.['finally'] === true) {
    return 'A finally job runs after all the others by itself.';
  }
  return source in jobs && target in jobs ? null : 'Both ends must be jobs of this file.';
}

/** Makes `target` wait for `source`: on success, failure or always, or for a switch's case. */
export function connect(text: string, source: string, handle: string, target: string): Change | null {
  if (connectionProblem(text, source, target) !== null) {
    return null;
  }
  const jobs = jobsOf(text);
  const sourceIsSwitch = (jobs[source] as Mapping | undefined)?.['type'] === 'switch';
  const entry = need(source, handle, sourceIsSwitch);
  if (needsOf(jobs[target]).some((existing) => sameNeed(existing, entry))) {
    return null;
  }
  return new Edits(text)
    .run((current) => appendItem(current, ['jobs', target, 'needs'], entry, { flow: true, first: true }))
    .done(`Connected ${source} to ${target}`, target);
}

/** The index in `needs` of `target` that a line from `source` (with `handle`) stands for. */
function needIndex(text: string, source: string, handle: string, target: string): number {
  const jobs = jobsOf(text);
  const sourceIsSwitch = (jobs[source] as Mapping | undefined)?.['type'] === 'switch';
  const entry = need(source, handle, sourceIsSwitch);
  return needsOf(jobs[target]).findIndex((existing) => sameNeed(existing, entry));
}

/** Removes what a line stands for; `needs` left empty goes away entirely. */
export function disconnect(text: string, source: string, handle: string, target: string): Change | null {
  const index = needIndex(text, source, handle, target);
  if (index === -1) {
    return null;
  }
  const remaining = needsOf(jobsOf(text)[target]).length - 1;
  return new Edits(text)
    .run((current) =>
      remaining === 0
        ? deleteIn(current, ['jobs', target, 'needs'])
        : removeItem(current, ['jobs', target, 'needs'], index),
    )
    .done(`Disconnected ${source} from ${target}`, target);
}

/** Deletes jobs with their positions and everything that waits for them. */
export function deleteJobs(text: string, ids: readonly string[]): Change | null {
  const doomed = new Set(ids.filter((id) => id !== triggerId && id in jobsOf(text)));
  if (doomed.size === 0) {
    return null;
  }
  const edits = new Edits(text);
  for (const [id, job] of Object.entries(jobsOf(text))) {
    if (doomed.has(id)) {
      continue;
    }
    // From the end, so the indexes still to remove stay valid.
    const needs = needsOf(job);
    const gone = needs
      .map((entry, index) => ({ entry, index }))
      .filter(({ entry }) => doomed.has(typeof entry === 'string' ? entry : String((entry as Mapping)['job'])))
      .map(({ index }) => index)
      .toReversed();
    if (gone.length === needs.length && gone.length > 0) {
      edits.run((current) => deleteIn(current, ['jobs', id, 'needs']));
    } else {
      for (const index of gone) {
        edits.run((current) => removeItem(current, ['jobs', id, 'needs'], index));
      }
    }
  }
  for (const id of doomed) {
    edits.run((current) => deleteIn(current, ['jobs', id]));
    edits.run((current) => deleteIn(current, ['x-editor', 'layout', id]));
  }
  return edits.done(doomed.size === 1 ? `Deleted ${[...doomed][0]}` : `Deleted ${doomed.size} jobs`);
}

/** Places every card anew and saves all positions. */
export function tidy(text: string): Change | null {
  const read = graphFromYaml(text);
  if (!read.graph) {
    return null;
  }
  return { ...movePositions(text, layOut(read.graph, { all: true })), summary: 'Tidied up the canvas' };
}

/**
 * Saves the place of every card that has none yet. Run before a change on the canvas: cards
 * without a saved place are laid out anew whenever the graph changes, so they would jump around
 * as soon as a job is added. Pinned once, everything stays where it is shown.
 */
export function pinUnplaced(text: string): string {
  const read = graphFromYaml(text);
  if (!read.graph) {
    return text;
  }
  const shown = layOut(read.graph);
  const unplaced = Object.fromEntries(Object.entries(shown).filter(([id]) => !(id in (read.graph?.positions ?? {}))));
  return Object.keys(unplaced).length === 0 ? text : movePositions(text, unplaced).text;
}
