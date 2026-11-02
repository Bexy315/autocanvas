import { describe, expect, it } from 'vitest';
import { graphFromYaml, type Graph } from './model';
import {
  addJob,
  connect,
  connectionProblem,
  deleteJobs,
  disconnect,
  movePositions,
  newJobId,
  tidy,
  type NodeTypeView,
} from './operations';

const examples = import.meta.glob<string>('../../../../schema/examples/*.yml', {
  query: '?raw',
  import: 'default',
  eager: true,
});

const file = `# Release
version: 1
jobs:
  build:   # compiles
    steps:
    - run: make
  test:
    needs: [build]
    steps:
    - run: make test
  route:
    type: switch
    needs: [test]
    expression: \${{ jobs.test.outputs.ok }}
    cases:
      - { name: ship, when: 'yes' }
      - { name: hold, default: true }
  deploy:
    needs: [{job: route, case: ship}]
    steps:
    - run: ./deploy
`;

function graph(text: string): Graph {
  const read = graphFromYaml(text);
  if (!read.graph) {
    throw new Error(read.error);
  }
  return read.graph;
}

const lines = (text: string) =>
  graph(text)
    .edges.filter((edge) => edge.kind !== 'start')
    .map((edge) => `${edge.source}:${edge.sourceHandle}->${edge.target}`);

const httpRequest: NodeTypeView = {
  name: 'http_request',
  description: 'Sends an HTTP request.',
  inputs: [
    { name: 'url', kind: 'Text', required: true, description: 'The address.', choices: [] },
    { name: 'method', kind: 'Text', required: false, description: 'The method.', choices: [] },
  ],
  outputs: [],
};

describe('moving cards', () => {
  it('saves positions, rounded, and keeps the rest of the file', () => {
    const moved = movePositions(file, { build: { x: 10.4, y: 20.6 }, test: { x: 0, y: 200 } });
    expect(moved.text.startsWith(file)).toBe(true);
    expect(moved.text.slice(file.length)).toBe(
      'x-editor:\n  layout:\n    build: {x: 10, y: 21}\n    test: {x: 0, y: 200}\n',
    );
    expect(graph(moved.text).positions).toEqual({ build: { x: 10, y: 21 }, test: { x: 0, y: 200 } });
    expect(moved.summary).toBe('Moved 2 cards');
    expect(movePositions(moved.text, { build: { x: 1, y: 1 } }).summary).toBe('Moved build');
  });
});

describe('adding jobs', () => {
  it('adds each kind under a free id, with its position', () => {
    let text = file;
    for (const kind of ['job', 'job', 'switch', 'join', 'finally'] as const) {
      text = addJob(text, kind, { x: 5, y: 5 }).text;
    }
    const read = graph(text);
    expect(read.jobs.map((job) => [job.id, job.kind, job.isFinally])).toEqual([
      ['build', 'step', false],
      ['test', 'step', false],
      ['route', 'switch', false],
      ['deploy', 'step', false],
      ['job', 'step', false],
      ['job2', 'step', false],
      ['switch', 'switch', false],
      ['join', 'join', false],
      ['finally', 'step', true],
    ]);
    expect(read.positions['job2']).toEqual({ x: 5, y: 5 });
    expect(text.startsWith(file)).toBe(true);
  });

  it('adds a job with a step of a node type and its required inputs to fill in', () => {
    const added = addJob(file, { nodeType: httpRequest }, { x: 0, y: 0 });
    expect(added).toMatchObject({ summary: 'Added http_request http-request', focus: 'http-request' });
    expect(added.text).toContain(
      '  http-request:\n    runs_on: linux\n    image: alpine:3.22\n    steps:\n    - type: http_request\n      with:\n        url: ""\n',
    );
  });

  it('starts the jobs of an empty file', () => {
    expect(graph(addJob('version: 1\n', 'join', { x: 0, y: 0 }).text).jobs.map((job) => job.id)).toEqual(['join']);
  });

  it('finds free ids', () => {
    expect(newJobId({}, 'job')).toBe('job');
    expect(newJobId({ job: 1, job2: 1 }, 'job')).toBe('job3');
  });
});

describe('connecting jobs', () => {
  it('writes success as a name, failure and always as objects, and a case for a switch', () => {
    let text = connect(file, 'build', 'failure', 'deploy')?.text ?? '';
    text = connect(text, 'test', 'always', 'deploy')?.text ?? '';
    text = connect(text, 'route', 'hold', 'build')?.text ?? '';
    text = connect(text, 'build', 'success', 'route')?.text ?? '';
    expect(text).toContain(
      '    needs: [{job: route, case: ship}, {job: build, on: failure}, {job: test, on: always}]\n',
    );
    expect(text).toContain('  build:   # compiles\n    needs: [{job: route, case: hold}]\n');
    expect(text).toContain('    needs: [test, build]\n');
    expect(lines(text)).toEqual(
      expect.arrayContaining([
        'build:failure->deploy',
        'test:always->deploy',
        'route:hold->build',
        'build:success->route',
      ]),
    );
  });

  it('ignores what is already there and refuses what cannot be', () => {
    expect(connect(file, 'build', 'success', 'test')).toBeNull();
    expect(connect(file, 'build', 'success', 'build')).toBeNull();
    expect(connectionProblem(file, 'trigger', 'build')).toBe('Jobs without needs start with the run already.');
    expect(connectionProblem(file, 'build', 'trigger')).toBe('Nothing runs before the trigger.');
    expect(connectionProblem(file, 'build', 'nope')).toBe('Both ends must be jobs of this file.');
    const withFinally = addJob(file, 'finally', { x: 0, y: 0 }).text;
    expect(connectionProblem(withFinally, 'build', 'finally')).toBe(
      'A finally job runs after all the others by itself.',
    );
  });

  it('disconnects one need, and removes needs when it was the last', () => {
    const fewer = disconnect(file, 'build', 'success', 'test');
    expect(fewer?.text).toBe(file.replace('    needs: [build]\n', ''));
    const twice = connect(file, 'build', 'failure', 'deploy')?.text ?? '';
    expect(disconnect(twice, 'route', 'ship', 'deploy')?.text).toContain('    needs: [{job: build, on: failure}]\n');
    expect(disconnect(file, 'deploy', 'success', 'test')).toBeNull();
  });
});

describe('deleting jobs', () => {
  it('removes the jobs, what waits for them and their positions', () => {
    const placed = movePositions(file, { test: { x: 1, y: 1 }, deploy: { x: 2, y: 2 } }).text;
    const deleted = deleteJobs(placed, ['test', 'trigger']);
    expect(deleted?.summary).toBe('Deleted test');
    const read = graph(deleted?.text ?? '');
    expect(read.jobs.map((job) => job.id)).toEqual(['build', 'route', 'deploy']);
    expect(read.jobs.flatMap((job) => job.problems)).toEqual([]);
    expect(read.positions).toEqual({ deploy: { x: 2, y: 2 } });
    expect(deleted?.text).toContain('# Release\n');
    expect(deleteJobs(file, ['nope'])).toBeNull();
  });

  it('keeps other needs of a job that waited for several', () => {
    const both = connect(file, 'build', 'failure', 'deploy')?.text ?? '';
    const deleted = deleteJobs(both, ['route', 'build']);
    expect(graph(deleted?.text ?? '').jobs.map((job) => job.id)).toEqual(['test', 'deploy']);
    expect(deleted?.summary).toBe('Deleted 2 jobs');
  });
});

describe('every example', () => {
  it.each(Object.entries(examples))('%s can be tidied, extended, connected and cut down', (_, source) => {
    const tidied = tidy(source)?.text ?? '';
    const read = graph(tidied);
    expect(Object.keys(read.positions).toSorted()).toEqual(['trigger', ...read.jobs.map((job) => job.id)].toSorted());
    const added = addJob(tidied, 'job', { x: 0, y: 0 });
    const first = read.jobs[0]?.id ?? '';
    const connected = connect(added.text, first, 'always', added.focus ?? '')?.text ?? '';
    expect(lines(connected)).toContain(`${first}:always->${added.focus ?? ''}`);
    const cut = deleteJobs(connected, [first]);
    expect(graph(cut?.text ?? '').jobs.flatMap((job) => job.problems)).toEqual([]);
    // Every step spliced the text; none had to write the whole file.
    const steps = [tidy(source), added, connect(added.text, first, 'always', added.focus ?? ''), cut];
    expect(steps.map((change) => change?.rewritten ?? false)).toEqual([false, false, false, false]);
  });

  it('cannot be tidied while unreadable', () => {
    expect(tidy('jobs: [')).toBeNull();
  });
});
