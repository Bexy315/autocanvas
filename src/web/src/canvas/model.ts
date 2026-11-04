/**
 * An automation file as a graph for the canvas: the trigger, the jobs and what each waits for.
 *
 * The YAML is the source of truth (ADR-0018); this reads it and never writes it. Parts the canvas
 * does not draw are simply not read. Harvested from the origin's flowConvert, rewritten for the
 * AutoCanvas format v1 (`needs`, `runs_on`, `triggers`, `type: switch | join`, `finally`).
 */
import { parseDocument } from 'yaml';

/** The id of the trigger card, also its key in `x-editor.layout`. */
export const triggerId = 'trigger';

export type JobKind = 'step' | 'switch' | 'join';

/** One step of a job, as its card lists it. */
export interface StepSummary {
  label: string;
  /** The node type of a `type:` step; absent for a script step. */
  nodeType?: string;
}

export interface CaseSummary {
  name: string;
  /** `= 'true'`, or `default`. */
  label: string;
}

export interface JobData {
  id: string;
  title: string;
  kind: JobKind;
  isFinally: boolean;
  runsOn?: string;
  image?: string;
  steps: StepSummary[];
  /** A switch's expression and cases; empty for other jobs. */
  expression?: string;
  cases: CaseSummary[];
  /** Short facts worth seeing on the card: services, reports, retry, timeout, condition. */
  hints: string[];
  /** What the canvas could not draw, such as a need of a job that does not exist. */
  problems: string[];
}

export type EdgeKind = 'success' | 'failure' | 'always' | 'case' | 'start' | 'finally';

export interface GraphEdge {
  id: string;
  source: string;
  target: string;
  /** The output it leaves from: `success`, `failure`, `always`, a case name, or `output` for the trigger. */
  sourceHandle: string;
  kind: EdgeKind;
  label?: string;
}

export interface Graph {
  /** What starts the automation, one line each. */
  triggers: string[];
  jobs: JobData[];
  edges: GraphEdge[];
  /** Positions saved in the file (`x-editor.layout`); the others are laid out. */
  positions: Record<string, { x: number; y: number }>;
}

export type GraphResult = { graph: Graph; error?: undefined } | { graph?: undefined; error: string };

type Mapping = Record<string, unknown>;

function isMapping(value: unknown): value is Mapping {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function text(value: unknown): string | undefined {
  return typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean'
    ? String(value)
    : undefined;
}

function short(value: string, length = 48): string {
  const line = value.trim().split('\n')[0] ?? '';
  return line.length > length || value.trim().includes('\n') ? `${line.slice(0, length - 1).trimEnd()}…` : line;
}

function triggerLines(triggers: unknown): string[] {
  if (!isMapping(triggers)) {
    return [];
  }
  const lines: string[] = [];
  if (triggers['manual'] === true) {
    lines.push('By hand');
  }
  if (triggers['webhook'] === true) {
    lines.push('Webhook');
  }
  for (const entry of Array.isArray(triggers['schedule']) ? triggers['schedule'] : []) {
    if (isMapping(entry) && text(entry['interval'])) {
      lines.push(`Every ${text(entry['interval'])}`);
    } else if (isMapping(entry) && text(entry['cron'])) {
      const zone = text(entry['timezone']);
      lines.push(`Cron ${text(entry['cron'])}${zone ? ` (${zone})` : ''}`);
    }
  }
  const completed = triggers['automation_completed'];
  if (isMapping(completed) && text(completed['automation'])) {
    lines.push(`After ${text(completed['automation'])} (${text(completed['on']) ?? 'success'})`);
  }
  if (triggers['push'] !== undefined) {
    lines.push('Push');
  }
  if (triggers['pull_request'] !== undefined) {
    lines.push('Pull request');
  }
  return lines;
}

function stepSummary(step: unknown, index: number): StepSummary {
  if (!isMapping(step)) {
    return { label: `Step ${index + 1}` };
  }
  const type = text(step['type']);
  const name = text(step['name']);
  if (type) {
    return { label: name ?? type, nodeType: type };
  }
  return { label: name ?? short(text(step['run']) ?? `Step ${index + 1}`) };
}

function hints(job: Mapping): string[] {
  const result: string[] = [];
  if (isMapping(job['services'])) {
    const names = Object.keys(job['services']);
    result.push(`${names.length === 1 ? 'Service' : 'Services'}: ${names.join(', ')}`);
  }
  if (Array.isArray(job['reports']) && job['reports'].length > 0) {
    result.push(job['reports'].length === 1 ? '1 report' : `${job['reports'].length} reports`);
  }
  if (isMapping(job['retry']) && text(job['retry']['max_attempts'])) {
    result.push(`Up to ${text(job['retry']['max_attempts'])} attempts`);
  }
  if (text(job['timeout_minutes'])) {
    result.push(`${text(job['timeout_minutes'])} min limit`);
  }
  if (text(job['if'])) {
    result.push(`If ${short(text(job['if']) ?? '', 32)}`);
  }
  return result;
}

function kindOf(job: Mapping): JobKind {
  return job['type'] === 'switch' ? 'switch' : job['type'] === 'join' ? 'join' : 'step';
}

function jobData(id: string, job: Mapping): JobData {
  const kind = kindOf(job);
  const cases = (Array.isArray(job['cases']) ? job['cases'] : []).flatMap((entry): CaseSummary[] => {
    const name = isMapping(entry) ? text(entry['name']) : undefined;
    if (!isMapping(entry) || !name) {
      return [];
    }
    return [{ name, label: entry['default'] === true ? 'default' : `= ${text(entry['when']) ?? ''}` }];
  });
  const runsOn = Array.isArray(job['runs_on']) ? job['runs_on'].map(String).join(', ') : text(job['runs_on']);
  const image = text(job['image']);
  const expression = text(job['expression']);
  return {
    id,
    title: text(job['name']) ?? id,
    kind,
    isFinally: job['finally'] === true,
    ...(runsOn ? { runsOn } : {}),
    ...(image ? { image } : {}),
    steps: (Array.isArray(job['steps']) ? job['steps'] : []).map(stepSummary),
    ...(expression ? { expression } : {}),
    cases,
    hints: kind === 'step' ? hints(job) : [],
    problems: [],
  };
}

interface Need {
  job: string;
  on: 'success' | 'failure' | 'always';
  case?: string;
}

function needsOf(job: Mapping): Need[] {
  const needs = job['needs'];
  return (Array.isArray(needs) ? needs : []).flatMap((need): Need[] => {
    if (typeof need === 'string') {
      return [{ job: need, on: 'success' }];
    }
    if (!isMapping(need) || !text(need['job'])) {
      return [];
    }
    const on = need['on'] === 'failure' || need['on'] === 'always' ? need['on'] : 'success';
    const caseName = text(need['case']);
    return [{ job: text(need['job']) ?? '', on, ...(caseName ? { case: caseName } : {}) }];
  });
}

/** The output a "then the finally job" line leaves from, or null when the job has none to spare. */
function endHandle(job: JobData | undefined): string | null {
  if (!job) {
    return 'output';
  }
  if (job.kind === 'join') {
    return 'success';
  }
  return job.kind === 'step' ? 'always' : null;
}

/**
 * The dashed lines into finally jobs: from every job nothing else waits for, or from the trigger
 * when there is no other job. Drawn, never stored: a finally job waits for every job, not these.
 */
function finallyEdges(jobs: JobData[], edges: GraphEdge[]): GraphEdge[] {
  const finals = jobs.filter((job) => job.isFinally);
  if (finals.length === 0) {
    return [];
  }
  const finalIds = new Set(finals.map((job) => job.id));
  const work = jobs.filter((job) => !job.isFinally);
  const waitedFor = new Set(
    edges.filter((edge) => !finalIds.has(edge.target) && edge.source !== triggerId).map((edge) => edge.source),
  );
  const leaves = work.filter((job) => !waitedFor.has(job.id));
  const sources: (JobData | undefined)[] = leaves.length > 0 ? leaves : [undefined];

  return finals.flatMap((target) =>
    sources.flatMap((source) => {
      const handle = endHandle(source);
      const from = source?.id ?? triggerId;
      return handle === null
        ? []
        : [
            {
              id: `end:${from}->${target.id}`,
              source: from,
              target: target.id,
              sourceHandle: handle,
              kind: 'finally' as const,
            },
          ];
    }),
  );
}

function positionsOf(data: Mapping): Record<string, { x: number; y: number }> {
  const editor = data['x-editor'];
  const layout = isMapping(editor) ? editor['layout'] : undefined;
  const positions: Record<string, { x: number; y: number }> = {};
  for (const [id, value] of Object.entries(isMapping(layout) ? layout : {})) {
    if (isMapping(value) && typeof value['x'] === 'number' && typeof value['y'] === 'number') {
      positions[id] = { x: value['x'], y: value['y'] };
    }
  }
  return positions;
}

/** Reads an automation file into the canvas's graph; an unreadable file gives the parser's first error. */
export function graphFromYaml(source: string): GraphResult {
  const document = parseDocument(source);
  const firstError = document.errors[0];
  if (firstError) {
    return { error: firstError.message };
  }
  const data: unknown = document.toJS();
  if (data !== null && data !== undefined && !isMapping(data)) {
    return { error: 'The file is not a mapping of keys.' };
  }
  const root = data ?? {};
  const jobsMap = isMapping(root['jobs']) ? root['jobs'] : {};
  const jobs = Object.entries(jobsMap).map(([id, job]) => jobData(id, isMapping(job) ? job : {}));
  const byId = new Map(jobs.map((job) => [job.id, job]));
  const edges: GraphEdge[] = [];

  for (const job of jobs) {
    const raw = jobsMap[job.id];
    if (job.isFinally) {
      continue;
    }
    const needs = isMapping(raw) ? needsOf(raw) : [];
    if (needs.length === 0) {
      // A job that waits for nothing starts with the run; the line is drawn, not stored.
      edges.push({ id: `start->${job.id}`, source: triggerId, target: job.id, sourceHandle: 'output', kind: 'start' });
      continue;
    }
    for (const need of needs) {
      const from = byId.get(need.job);
      if (!from) {
        job.problems.push(`Needs ${need.job}, which is not a job of this file.`);
        continue;
      }
      if (need.case !== undefined) {
        edges.push({
          id: `${need.job}:${need.case}->${job.id}`,
          source: need.job,
          target: job.id,
          sourceHandle: need.case,
          kind: 'case',
          label: need.case,
        });
        continue;
      }
      edges.push({
        id: `${need.job}:${need.on}->${job.id}`,
        source: need.job,
        target: job.id,
        sourceHandle: need.on,
        kind: need.on,
        ...(need.on === 'success' ? {} : { label: need.on === 'failure' ? 'on failure' : 'always' }),
      });
    }
  }

  return {
    graph: {
      triggers: triggerLines(root['triggers']),
      jobs,
      edges: [...edges, ...finallyEdges(jobs, edges)],
      positions: positionsOf(root),
    },
  };
}
