import { describe, expect, it } from 'vitest';
import { graphFromYaml, type Graph } from './model';
import {
  addJob,
  addStep,
  connect,
  connectionProblem,
  deleteJobs,
  disconnect,
  movePositions,
  moveStep,
  newJobId,
  removeStep,
  renameJob,
  renameProblem,
  setField,
  setNeeds,
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

    // And through the panel: a field set and cleared, a step added, the job renamed.
    const imaged = setField(added.text, ['jobs', added.focus ?? '', 'image'], 'node:24', 'Changed image');
    const stepped = addStep(imaged?.text ?? '', added.focus ?? '', 'script');
    const renamed = renameJob(stepped.text, first, 'renamed-first');
    expect([imaged, stepped, renamed].map((change) => change?.rewritten ?? false)).toEqual([false, false, false]);
    expect(graph(renamed?.text ?? '').jobs.map((job) => job.id)).toContain('renamed-first');
  });

  it('cannot be tidied while unreadable', () => {
    expect(tidy('jobs: [')).toBeNull();
  });
});

describe('fields from the panel', () => {
  it('set a value in place, keeping the rest of the file', () => {
    const change = setField(file, ['jobs', 'build', 'image'], 'alpine:3.22', 'Changed image of build');
    // A new key goes where it usually is: the image before the steps.
    expect(change?.text).toBe(
      file.replace('  build:   # compiles\n', '  build:   # compiles\n    image: alpine:3.22\n'),
    );
    expect(change?.summary).toBe('Changed image of build');
    expect(setField(change?.text ?? '', ['jobs', 'build', 'image'], 'alpine:3.22', 'Same')).toBeNull();
  });

  it('write new lists of plain values in flow style, and keep a list in its style', () => {
    const labels = setField(file, ['jobs', 'build', 'runs_on'], ['linux', 'docker'], 'Labels');
    expect(labels?.text).toContain('    runs_on: [linux, docker]\n');
    const needs = setField(file, ['jobs', 'test', 'needs'], ['build', 'lint'], 'Needs');
    expect(needs?.text).toContain('    needs: [build, lint]\n');
  });

  it('remove the key for an empty value, and mappings left empty with it', () => {
    const withUrl = 'version: 1\njobs:\n  a:\n    steps:\n    - type: http_request\n      with:\n        url: x\n';
    const cleared = setField(withUrl, ['jobs', 'a', 'steps', 0, 'with', 'url'], '', 'Cleared url');
    expect(cleared?.text).toBe('version: 1\njobs:\n  a:\n    steps:\n    - type: http_request\n');
    expect(setField(withUrl, ['jobs', 'a', 'image'], '', 'Nothing')).toBeNull();
    const job = 'jobs:\n  a:\n    retry:\n      max_attempts: 3\n';
    // The job itself stays, even when it has nothing left.
    expect(setField(job, ['jobs', 'a', 'retry', 'max_attempts'], undefined, 'No retry')?.text).toBe('jobs:\n  a: {}\n');
  });

  it('write a script of several lines as a block', () => {
    const change = setField(file, ['jobs', 'build', 'steps', 0, 'run'], 'make\nmake check\n', 'Script');
    expect(change?.text).toContain('    - run: |\n        make\n        make check\n  test:');
  });
});

describe('steps from the panel', () => {
  it('are added at the end, as a script or a node type with its required inputs', () => {
    const script = addStep(file, 'build', 'script');
    expect(script.text).toContain('    - run: make\n    - run: echo "Hello"\n  test:');
    const node = addStep(file, 'build', { nodeType: httpRequest });
    expect(node.text).toContain('    - type: http_request\n      with:\n        url: ""\n');
  });

  it('move with their lines and are deleted', () => {
    const two = addStep(file, 'build', 'script').text;
    expect(moveStep(two, 'build', 1, 0)?.text).toContain('    - run: echo "Hello"\n    - run: make\n');
    expect(moveStep(two, 'build', 0, 0)).toBeNull();
    expect(removeStep(two, 'build', 0)?.text).toContain('    steps:\n    - run: echo "Hello"\n');
  });
});

describe('needs from the panel', () => {
  it('are written like drawn lines, first in the job', () => {
    const change = setNeeds(file, 'build', [{ job: 'test', on: 'failure' }, { job: 'route', case: 'hold' }, 'deploy']);
    expect(change?.text).toContain(
      '  build:   # compiles\n    needs: [{job: test, on: failure}, {job: route, case: hold}, deploy]\n    steps:',
    );
    expect(setNeeds(file, 'test', [{ job: 'build', on: 'success' }])).toBeNull();
    expect(setNeeds(file, 'test', [])?.text).not.toContain('needs: [build]');
  });
});

describe('renaming a job', () => {
  it('takes its needs, its place and expressions with it', () => {
    const placed = movePositions(file, { test: { x: 1, y: 2 } }).text;
    const withIf = placed.replace('    - run: ./deploy\n', '    - run: ./deploy\n      if: jobs.test.outputs.ok\n');
    const change = renameJob(withIf, 'test', 'check');
    expect(change?.rewritten).toBeUndefined();
    const text = change?.text ?? '';
    expect(text).toContain('  check:\n    needs: [build]');
    expect(text).toContain('    needs: [check]\n    expression: ${{ jobs.check.outputs.ok }}');
    expect(text).toContain('      if: jobs.check.outputs.ok');
    expect(text).toContain('    check: {x: 1, y: 2}');
    expect(lines(text)).toEqual(lines(file).map((line) => line.replaceAll('test', 'check')));
  });

  it('follows needs written as objects, and leaves similar ids alone', () => {
    const text =
      'jobs:\n  a:\n    steps: [{run: "${{ jobs.ab.outputs.x }}"}]\n  ab:\n    needs: [{job: a, on: always}]\n    steps: [{run: x}]\n';
    const change = renameJob(text, 'a', 'first');
    expect(change?.text).toBe(text.replace('  a:\n', '  first:\n').replace('{job: a,', '{job: first,'));
  });

  it('is refused for ids that are taken or not allowed', () => {
    expect(renameProblem(file, 'test', 'build')).toBe('There is a job build already.');
    expect(renameProblem(file, 'test', 'Test')).toMatch(/^An id starts with a lower-case letter/);
    expect(renameProblem(file, 'test', 'test')).toBeNull();
    expect(renameJob(file, 'test', 'build')).toBeNull();
    expect(renameJob(file, 'missing', 'other')).toBeNull();
  });
});
