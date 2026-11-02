import { describe, expect, it } from 'vitest';
import { layOut } from './layout';
import { jobAtLine, jobLines } from './lines';
import { graphFromYaml, triggerId, type Graph } from './model';

/** The valid examples the schema ships, which the canvas must all draw. */
const examples = import.meta.glob<string>('../../../../schema/examples/*.yml', {
  query: '?raw',
  import: 'default',
  eager: true,
});

function graph(source: string): Graph {
  const result = graphFromYaml(source);
  if (!result.graph) {
    throw new Error(result.error);
  }
  return result.graph;
}

const edges = (source: string) =>
  graph(source).edges.map((edge) => `${edge.source}:${edge.sourceHandle}->${edge.target}`);

describe('reading a file as a graph', () => {
  it.each(Object.entries(examples))('draws the example %s', (_, source) => {
    const read = graph(source);
    expect(read.jobs.length).toBeGreaterThan(0);
    expect(read.jobs.flatMap((job) => job.problems)).toEqual([]);
    const ids = new Set([triggerId, ...read.jobs.map((job) => job.id)]);
    for (const edge of read.edges) {
      expect(ids.has(edge.source) && ids.has(edge.target)).toBe(true);
    }
  });

  it('turns needs into lines from the right output, and starts jobs without needs at the trigger', () => {
    const source = `version: 1
jobs:
  build:
    steps: [{ run: make }]
  test:
    needs: [build]
    steps: [{ run: make test }]
  notify:
    needs: [{ job: test, on: failure }]
    steps: [{ run: echo broken }]
  clean:
    needs: [{ job: test, on: always }]
    steps: [{ run: rm -rf out }]
`;
    expect(edges(source)).toEqual([
      'trigger:output->build',
      'build:success->test',
      'test:failure->notify',
      'test:always->clean',
    ]);
    expect(graph(source).edges.find((edge) => edge.target === 'notify')?.label).toBe('on failure');
  });

  it('draws a switch with one output per case', () => {
    const source = `version: 1
jobs:
  check:
    steps: [{ run: ./check }]
  route:
    type: switch
    needs: [check]
    expression: \${{ jobs.check.outputs.changed }}
    cases:
      - name: update
        when: 'true'
      - name: nothing
        default: true
  update:
    needs: [{ job: route, case: update }]
    steps: [{ run: ./update }]
`;
    const read = graph(source);
    const route = read.jobs.find((job) => job.id === 'route');
    expect(route).toMatchObject({
      kind: 'switch',
      expression: '${{ jobs.check.outputs.changed }}',
      cases: [
        { name: 'update', label: '= true' },
        { name: 'nothing', label: 'default' },
      ],
    });
    expect(read.edges.find((edge) => edge.target === 'update')).toMatchObject({
      kind: 'case',
      sourceHandle: 'update',
      label: 'update',
    });
  });

  it('draws the finally job after the jobs nothing waits for, without storing that', () => {
    const source = `version: 1
jobs:
  build:
    steps: [{ run: make }]
  join:
    type: join
    needs: [build]
  report:
    finally: true
    steps: [{ run: ./report }]
`;
    const read = graph(source);
    expect(read.jobs.find((job) => job.id === 'report')?.isFinally).toBe(true);
    expect(
      read.edges.filter((edge) => edge.kind === 'finally').map((edge) => `${edge.source}:${edge.sourceHandle}`),
    ).toEqual(['join:success']);
    const positions = layOut(read);
    expect(positions['report']?.y).toBeGreaterThan(positions['join']?.y ?? Infinity);
  });

  it('draws a finally job after the trigger when it is the only job', () => {
    expect(edges('version: 1\njobs:\n  report:\n    finally: true\n    steps: [{ run: x }]\n')).toEqual([
      'trigger:output->report',
    ]);
  });

  it('summarises the triggers and each job', () => {
    const read = graph(`version: 1
triggers:
  manual: true
  webhook: true
  schedule:
    - cron: '0 3 * * 1-5'
      timezone: Europe/Berlin
    - interval: 15m
  automation_completed:
    automation: build
    on: failure
jobs:
  deploy:
    name: Deploy the shop
    runs_on: [linux, x64]
    image: alpine:3.22
    timeout_minutes: 10
    if: \${{ trigger.branch == 'main' }}
    retry: { max_attempts: 3 }
    services: { db: { preset: postgres } }
    reports: [{ format: junit, path: out/*.xml }]
    steps:
      - name: Upload
        run: ./upload
      - type: http_request
        with: { url: https://example.com }
      - run: |
          a very long first line of a script that goes on and on and on
          and a second one
`);
    expect(read.triggers).toEqual([
      'By hand',
      'Webhook',
      'Cron 0 3 * * 1-5 (Europe/Berlin)',
      'Every 15m',
      'After build (failure)',
    ]);
    const deploy = read.jobs[0];
    expect(deploy).toMatchObject({ title: 'Deploy the shop', runsOn: 'linux, x64', image: 'alpine:3.22' });
    expect(deploy?.steps.map((step) => step.label)).toEqual([
      'Upload',
      'http_request',
      'a very long first line of a script that goes on…',
    ]);
    expect(deploy?.hints).toEqual([
      'Service: db',
      '1 report',
      'Up to 3 attempts',
      '10 min limit',
      "If ${{ trigger.branch == 'main' }}",
    ]);
  });

  it('marks a need of a job that does not exist, instead of dropping it silently', () => {
    const read = graph('version: 1\njobs:\n  build:\n    needs: [tset]\n    steps: [{ run: make }]\n');
    expect(read.jobs[0]?.problems).toEqual(['Needs tset, which is not a job of this file.']);
    expect(read.edges).toEqual([]);
  });

  it('reads files without jobs or triggers, and refuses what is not YAML', () => {
    expect(graph('')).toEqual({ triggers: [], jobs: [], edges: [], positions: {} });
    expect(graphFromYaml('jobs: [').error).toBeTruthy();
    expect(graphFromYaml('- just\n- a list\n').error).toBe('The file is not a mapping of keys.');
  });
});

describe('placing the cards', () => {
  it('keeps saved positions and lays out the others', () => {
    const read = graph(`version: 1
jobs:
  build:
    steps: [{ run: make }]
  test:
    needs: [build]
    steps: [{ run: make test }]
x-editor:
  layout:
    build: { x: 500, y: 40 }
    nonsense: 3
`);
    const positions = layOut(read);
    expect(positions['build']).toEqual({ x: 500, y: 40 });
    expect(positions['test']).toBeDefined();
    expect(positions[triggerId]).toBeDefined();
    expect(Object.keys(read.positions)).toEqual(['build']);
  });
});

describe('the lines of each job', () => {
  const source = `version: 1
# the jobs
jobs:
  build:
    steps:
      - run: make

  test: { needs: [build], steps: [{ run: make test }] }
  # a comment between jobs
  deploy:
    steps:
      - run: ./deploy
`;

  it('span from the key to the last line of the job', () => {
    expect(Object.fromEntries(jobLines(source))).toEqual({
      build: { from: 4, to: 6 },
      test: { from: 8, to: 8 },
      deploy: { from: 10, to: 12 },
    });
  });

  it('find the job a line is in', () => {
    const ranges = jobLines(source);
    expect(jobAtLine(ranges, 5)).toBe('build');
    expect(jobAtLine(ranges, 8)).toBe('test');
    expect(jobAtLine(ranges, 1)).toBeNull();
  });

  it('are empty for files without jobs', () => {
    expect(jobLines('version: 1\n').size).toBe(0);
    expect(jobLines('- a\n').size).toBe(0);
    expect(jobLines('jobs: 3\n').size).toBe(0);
  });
});
