/**
 * What the canvas does to the file. Each operation takes the current text and returns the new one,
 * changed only where it must be (yamlEdit), so the YAML stays the single source of truth and a
 * canvas change is one step in the editor's undo history like typing is.
 */
import { isMap, isScalar, isSeq, parseDocument } from 'yaml';
import type { components } from '../api/schema';
import { layOut, type Positions } from './layout';
import { graphFromYaml, triggerId } from './model';
import {
  appendItem,
  deleteIn,
  insertItem,
  moveItem,
  removeItem,
  renameKey,
  setIn,
  type EditResult,
  type Path,
} from './yamlEdit';

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

/** A step of a node type, with its required inputs left empty for the panel to fill. */
function nodeStep(nodeType: NodeTypeView): Mapping {
  const required = nodeType.inputs.filter((input) => input.required);
  const step: Mapping = { type: nodeType.name };
  if (required.length > 0) {
    step['with'] = Object.fromEntries(required.map((input) => [input.name, '']));
  }
  return step;
}

/** The job a palette entry adds; a node type's required inputs are left empty for the panel to fill. */
function template(kind: NewJobKind): { base: string; job: Mapping } {
  if (typeof kind === 'object') {
    return { base: kind.nodeType.name.replaceAll('_', '-'), job: { ...runner, steps: [nodeStep(kind.nodeType)] } };
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

  get current(): string {
    return this.text;
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

const orders = {
  file: [
    'version',
    'name',
    'description',
    'triggers',
    'variables',
    'timeout_minutes',
    'concurrency',
    'changed_when',
    'jobs',
    'x-editor',
  ],
  triggers: ['manual', 'webhook', 'push', 'pull_request', 'schedule', 'automation_completed'],
  job: [
    'name',
    'needs',
    'if',
    'type',
    'finally',
    'on_cancel',
    'runs_on',
    'image',
    'checkout',
    'env',
    'services',
    'expression',
    'cases',
    'steps',
    'outputs',
    'reports',
    'timeout_minutes',
    'retry',
  ],
  step: [
    'name',
    'id',
    'if',
    'run',
    'type',
    'shell',
    'working_directory',
    'with',
    'env',
    'continue_on_error',
    'timeout_minutes',
    'outputs',
  ],
  retry: ['max_attempts', 'delay_seconds'],
  service: ['preset', 'version', 'image', 'env', 'ready'],
  checkout: ['repository', 'ref', 'path', 'depth'],
  report: ['format', 'path', 'name'],
  schedule: ['cron', 'timezone', 'interval'],
  push: ['branches', 'tags', 'paths'],
  completed: ['automation', 'on'],
} as const;

/** The usual order of keys in the mapping at `path`, as the schema and its examples write them. */
export function keyOrder(path: Path): readonly string[] | undefined {
  const [first, , third, fourth] = path;
  if (path.length === 0) {
    return orders.file;
  }
  if (first === 'triggers') {
    return path.length === 1
      ? orders.triggers
      : { push: orders.push, schedule: orders.schedule, automation_completed: orders.completed }[String(path[1])];
  }
  if (first !== 'jobs') {
    return undefined;
  }
  if (path.length === 2) {
    return orders.job;
  }
  if (path.length === 3 && third === 'retry') {
    return orders.retry;
  }
  if (path.length === 4 && typeof fourth === 'number') {
    return { steps: orders.step, checkout: orders.checkout, reports: orders.report }[String(third)];
  }
  return path.length === 4 && third === 'services' ? orders.service : undefined;
}

/** Nothing to write: such a value removes its key instead. */
function isEmpty(value: unknown): boolean {
  if (value === undefined || value === null || value === '') {
    return true;
  }
  if (Array.isArray(value)) {
    return value.length === 0;
  }
  return typeof value === 'object' && Object.keys(value).length === 0;
}

const isScalarValue = (value: unknown) => value === null || typeof value !== 'object';

/**
 * Sets one value from the panel, in the style the file already has: a list or mapping written in
 * flow style stays so, and a new list of plain values is written as `[a, b]`. An empty value removes
 * the key, and mappings it leaves empty go too (`with: {}` after its last input), up to the job.
 */
export function setField(text: string, path: Path, value: unknown, summary: string): Change | null {
  const document = parseDocument(text);
  const edits = new Edits(text);
  const parentNode = document.getIn(path.slice(0, -1), true);
  // A list item keeps its last key, empty, rather than becoming `{}`: a script cleared to be typed
  // anew stays `run: ""` until the first character.
  const lastOfItem =
    typeof path.at(-2) === 'number' && isMap(parentNode) && parentNode.items.length === 1 && typeof value === 'string';
  if (isEmpty(value) && !lastOfItem) {
    if (!document.hasIn([...path])) {
      return null;
    }
    edits.run((current) => deleteIn(current, path));
    // Up through mappings left empty; never past a job or a list item.
    const floor = path[0] === 'jobs' ? 2 : 0;
    let parent = path.slice(0, -1);
    while (parent.length > floor && typeof parent.at(-1) === 'string') {
      const above = parent;
      const node = parseDocument(edits.current).getIn([...above], true);
      if (!isMap(node) || node.items.length > 0) {
        break;
      }
      edits.run((current) => deleteIn(current, above));
      parent = parent.slice(0, -1);
    }
  } else {
    const existing = document.getIn([...path], true);
    const flow =
      isMap(existing) || isSeq(existing) ? existing.flow === true : Array.isArray(value) && value.every(isScalarValue);
    if (isScalar(existing) && existing.value === value) {
      return null;
    }
    edits.run((current) => setIn(current, path, value, { flow, order: keyOrder }));
  }
  const change = edits.done(summary);
  return change.text === text ? null : change;
}

/** What a step added in the panel starts as. */
export type NewStepKind = 'script' | { nodeType: NodeTypeView };

/** Adds a step at the end of a job. */
export function addStep(text: string, job: string, kind: NewStepKind): Change {
  const step = kind === 'script' ? { run: 'echo "Hello"' } : nodeStep(kind.nodeType);
  return new Edits(text)
    .run((current) => appendItem(current, ['jobs', job, 'steps'], step))
    .done(`Added a ${kind === 'script' ? 'script' : kind.nodeType.name} step to ${job}`);
}

export function moveStep(text: string, job: string, from: number, to: number): Change | null {
  const change = new Edits(text)
    .run((current) => moveItem(current, ['jobs', job, 'steps'], from, to))
    .done(`Moved step ${from + 1} of ${job} to place ${to + 1}`);
  return change.text === text ? null : change;
}

export function removeStep(text: string, job: string, index: number): Change | null {
  const change = new Edits(text)
    .run((current) => removeItem(current, ['jobs', job, 'steps'], index))
    .done(`Deleted step ${index + 1} of ${job}`);
  return change.text === text ? null : change;
}

/**
 * Replaces what a job waits for: the keyboard's way to connect jobs. Written like lines drawn on
 * the canvas (a name for success, `{job, on}` or `{job, case}` otherwise), first in the job.
 */
export function setNeeds(text: string, job: string, needs: readonly unknown[]): Change | null {
  const normal = needs.map((entry) => {
    if (typeof entry === 'string') {
      return entry;
    }
    const { job: source, on, case: name } = entry as Mapping;
    if (typeof name === 'string' && name !== '') {
      return { job: source, case: name };
    }
    return on === 'failure' || on === 'always' ? { job: source, on } : source;
  });
  if (normal.length === 0) {
    return setField(text, ['jobs', job, 'needs'], [], `${job} no longer waits for other jobs`);
  }
  const existing = parseDocument(text).getIn(['jobs', job, 'needs'], true);
  const change = new Edits(text)
    .run((current) =>
      setIn(current, ['jobs', job, 'needs'], normal, { flow: !isSeq(existing) || existing.flow === true, first: true }),
    )
    .done(`Changed what ${job} waits for`);
  return change.text === text ? null : change;
}

const identifier = /^[a-z][a-z0-9_-]{0,62}$/;

/** Why a job cannot get the id `next`, or null when it can. */
export function renameProblem(text: string, old: string, next: string): string | null {
  if (next === old) {
    return null;
  }
  if (!identifier.test(next)) {
    return "An id starts with a lower-case letter, followed by lower-case letters, digits, '_' or '-'.";
  }
  return next in jobsOf(text) ? `There is a job ${next} already.` : null;
}

const escaped = (value: string) => value.replaceAll(/[.*+?^${}()|[\]\\]/g, String.raw`\$&`);

/** `jobs.OLD` in an expression's text, where OLD is the whole id. */
function referenceTo(id: string): RegExp {
  return new RegExp(String.raw`(?<![\w.-])jobs\.${escaped(id)}(?![\w-])`, 'g');
}

/** Points expressions at the new id: inside `${{ }}`, and in `if:`, where the braces are optional. */
function renameInExpressions(text: string, old: string, next: string): string {
  const reference = referenceTo(old);
  const inBraces = text.replaceAll(/\$\{\{[\s\S]*?\}\}/g, (expression) =>
    expression.replaceAll(reference, `jobs.${next}`),
  );
  return inBraces.replaceAll(
    /^(\s*(?:-\s+)?if:)(.*)$/gm,
    (_line, key: string, rest: string) => `${key}${rest.replaceAll(reference, `jobs.${next}`)}`,
  );
}

/**
 * Gives a job another id, and everything that names it follows: the `needs` of other jobs, its place
 * on the canvas and `jobs.OLD` in expressions.
 */
export function renameJob(text: string, old: string, next: string): Change | null {
  if (next === old || renameProblem(text, old, next) !== null || !(old in jobsOf(text))) {
    return null;
  }
  const edits = new Edits(text).run((current) => renameKey(current, ['jobs', old], next));
  if (parseDocument(text).hasIn(['x-editor', 'layout', old])) {
    edits.run((current) => renameKey(current, ['x-editor', 'layout', old], next));
  }
  for (const [id, job] of Object.entries(jobsOf(edits.current))) {
    needsOf(job).forEach((entry, index) => {
      if (entry === old) {
        edits.run((current) => setIn(current, ['jobs', id, 'needs', index], next));
      } else if (typeof entry === 'object' && entry !== null && (entry as Mapping)['job'] === old) {
        edits.run((current) => setIn(current, ['jobs', id, 'needs', index, 'job'], next));
      }
    });
  }
  edits.run((current) => {
    const renamed = renameInExpressions(current, old, next);
    return { text: parseDocument(renamed).errors.length === 0 ? renamed : current, fallback: false };
  });
  return edits.done(`Renamed ${old} to ${next}`, next);
}

/** Whether the items of a list are written in flow style, as `- { name: a, when: b }`. */
function itemsInFlow(list: unknown): boolean {
  return (
    isSeq(list) &&
    list.items.length > 0 &&
    list.items.every((item) => !(isMap(item) || isSeq(item)) || item.flow === true)
  );
}

/**
 * Adds an item to a list from the panel (a case, a schedule, a report), before `index` or at the
 * end, written like the items around it; a list that is not there yet is created.
 */
export function addListItem(text: string, path: Path, value: unknown, summary: string, index?: number): Change | null {
  const list = parseDocument(text).getIn([...path], true);
  if (!isSeq(list)) {
    return setField(text, path, [value], summary);
  }
  const change = new Edits(text)
    .run((current) => insertItem(current, path, index ?? list.items.length, value, { flow: itemsInFlow(list) }))
    .done(summary);
  return change.text === text ? null : change;
}

/** Removes an item of a list from the panel; a list left empty goes with it. */
export function removeListItem(text: string, path: Path, index: number, summary: string): Change | null {
  const list = parseDocument(text).getIn([...path], true);
  if (!isSeq(list) || index < 0 || index >= list.items.length) {
    return null;
  }
  if (list.items.length === 1) {
    return setField(text, path, undefined, summary);
  }
  return new Edits(text).run((current) => removeItem(current, path, index)).done(summary);
}

/** Moves an item of a list from the panel, with its lines and comments. */
export function moveListItem(text: string, path: Path, from: number, to: number, summary: string): Change | null {
  const change = new Edits(text).run((current) => moveItem(current, path, from, to)).done(summary);
  return change.text === text ? null : change;
}
