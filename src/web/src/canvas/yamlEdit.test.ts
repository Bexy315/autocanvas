import { describe, expect, it } from 'vitest';
import { appendItem, deleteIn, insertItem, moveItem, removeItem, renameKey, setIn } from './yamlEdit';

/** A hand-written file with everything a rewrite would normalise. */
const handWritten = `# Release pipeline
version: 1   # the format

triggers:
  manual: true
jobs:
  build:
    needs: [lint]  # flow, unpadded
    steps:
    - run: "make"
    - run: 'make test'

  lint: {steps: [{run: lint}]}
x-editor:
  layout:
    build: { x: 10, y: 20 }
`;

/** The lines of `after` that are not in `before`, and the other way round. */
function changed(before: string, after: string) {
  const a = before.split('\n');
  const b = after.split('\n');
  return { removed: a.filter((line) => !b.includes(line)), added: b.filter((line) => !a.includes(line)) };
}

describe('setting a value', () => {
  it('replaces a scalar and nothing else', () => {
    const result = setIn(handWritten, ['triggers', 'manual'], false);
    expect(result.fallback).toBe(false);
    expect(changed(handWritten, result.text)).toEqual({ removed: ['  manual: true'], added: ['  manual: false'] });
  });

  it('replaces a flow mapping inline, keeping a comment after it', () => {
    const result = setIn(handWritten, ['x-editor', 'layout', 'build'], { x: 300, y: 40 }, { flow: true });
    expect(changed(handWritten, result.text)).toEqual({
      removed: ['    build: { x: 10, y: 20 }'],
      added: ['    build: {x: 300, y: 40}'],
    });
    const commented = setIn(handWritten, ['jobs', 'build', 'needs'], ['lint', 'test'], { flow: true });
    expect(commented.text).toContain('    needs: [lint, test]  # flow, unpadded\n');
  });

  it('adds a pair at the end of a block mapping, with its indentation', () => {
    const result = setIn(handWritten, ['x-editor', 'layout', 'lint'], { x: 0, y: 0 }, { flow: true });
    expect(result.fallback).toBe(false);
    expect(result.text).toBe(
      handWritten.replace('    build: { x: 10, y: 20 }\n', '    build: { x: 10, y: 20 }\n    lint: {x: 0, y: 0}\n'),
    );
  });

  it('adds a block below the last pair of a mapping, before blank lines and the next key', () => {
    const result = setIn(handWritten, ['jobs', 'build', 'timeout_minutes'], 10);
    expect(result.text).toContain("    - run: 'make test'\n    timeout_minutes: 10\n\n  lint:");
  });

  it('writes a new job as a block in the file\u2019s list style', () => {
    const result = setIn(handWritten, ['jobs', 'deploy'], { needs: ['build'], steps: [{ run: './deploy' }] });
    expect(result.fallback).toBe(false);
    expect(result.text).toContain(`  lint: {steps: [{run: lint}]}
  deploy:
    needs:
    - build
    steps:
    - run: ./deploy
x-editor:`);
  });

  it('indents lists when the file does', () => {
    const indented = 'jobs:\n  a:\n    steps:\n      - run: x\n';
    expect(setIn(indented, ['jobs', 'b'], { steps: [{ run: 'y' }] }).text).toBe(
      'jobs:\n  a:\n    steps:\n      - run: x\n  b:\n    steps:\n      - run: y\n',
    );
  });

  it('creates missing mappings on the way, at the end of the file', () => {
    const result = setIn(
      'version: 1\njobs:\n  a:\n    steps: []\n',
      ['x-editor', 'layout', 'a'],
      { x: 1, y: 2 },
      { flow: true },
    );
    expect(result.text).toBe('version: 1\njobs:\n  a:\n    steps: []\nx-editor:\n  layout:\n    a: {x: 1, y: 2}\n');
  });

  it('adds to a flow mapping by writing only that mapping', () => {
    const result = setIn(handWritten, ['jobs', 'lint', 'runs_on'], 'linux');
    expect(changed(handWritten, result.text)).toEqual({
      removed: ['  lint: {steps: [{run: lint}]}'],
      added: ['  lint: {steps: [{run: lint}], runs_on: linux}'],
    });
  });

  it('replaces an empty value', () => {
    expect(setIn('jobs:\n  a:\nx: 1\n', ['jobs', 'a'], { steps: [] }).text).toBe('jobs:\n  a:\n    steps: []\nx: 1\n');
  });

  it('starts an empty file, and one without a final newline', () => {
    expect(setIn('', ['jobs', 'a'], { steps: [] }).text).toBe('jobs:\n  a:\n    steps: []\n');
    expect(setIn('version: 1', ['name'], 'Build').text).toBe('version: 1\nname: Build\n');
  });

  it('turns a block value into a scalar and back', () => {
    const scalar = setIn(handWritten, ['jobs', 'build', 'steps'], 'none');
    expect(scalar.text).toContain('    steps: none\n\n  lint:');
    const block = setIn('a:\n  b: 1\n', ['a', 'b'], { c: [1, 2] });
    // Without block lists in the file to go by, lists are indented, as most YAML is written.
    expect(block.text).toBe('a:\n  b:\n    c:\n      - 1\n      - 2\n');
  });

  it('refuses files that cannot be read, and paths through lists', () => {
    expect(() => setIn('a: [', ['b'], 1)).toThrow('cannot be read');
    expect(() => deleteIn('a: [', ['b'])).toThrow('cannot be read');
    expect(() => setIn('a: [1, 2]\n', ['a', 'b'], 3)).toThrow('Expected a valid index');
  });
});

describe('removing a value', () => {
  it('removes the lines of a pair and nothing else', () => {
    const result = deleteIn(handWritten, ['jobs', 'build', 'needs']);
    expect(result.fallback).toBe(false);
    expect(changed(handWritten, result.text)).toEqual({ removed: ['    needs: [lint]  # flow, unpadded'], added: [] });
  });

  it('removes a whole job with its block', () => {
    const result = deleteIn(handWritten, ['jobs', 'build']);
    expect(result.text).toBe(handWritten.replace(/  build:\n[\s\S]*?\n\n/, '\n'));
  });

  it('writes a flow mapping without the pair', () => {
    expect(deleteIn('a: {x: 1, y: 2}  # c\n', ['a', 'x']).text).toBe('a: {y: 2}  # c\n');
  });

  it('leaves an empty mapping as {}', () => {
    expect(deleteIn('x-editor:\n  layout:\n    a: {x: 1, y: 2}\n', ['x-editor', 'layout', 'a']).text).toBe(
      'x-editor:\n  layout: {}\n',
    );
  });

  it('does nothing for a path that is not there', () => {
    expect(deleteIn(handWritten, ['jobs', 'nope'])).toEqual({ text: handWritten, fallback: false });
  });

  it('removes a list item by its index', () => {
    expect(deleteIn('a: [1, 2, 3]\n', ['a', 1]).text).toBe('a: [1, 3]\n');
  });
});

describe('lists', () => {
  it('get an item appended inline when written in flow style', () => {
    const result = appendItem(handWritten, ['jobs', 'build', 'needs'], { job: 'test', on: 'failure' });
    expect(result.text).toContain('    needs: [lint, {job: test, on: failure}]  # flow, unpadded\n');
  });

  it('get a line appended when written as a block, in the file\u2019s indentation', () => {
    const result = appendItem(handWritten, ['jobs', 'build', 'steps'], { run: 'make dist', name: 'Package' });
    expect(result.fallback).toBe(false);
    expect(result.text).toContain("    - run: 'make test'\n    - run: make dist\n      name: Package\n\n");
    const scalar = appendItem('a:\n  - x\n', ['a'], 'y');
    expect(scalar.text).toBe('a:\n  - x\n  - y\n');
  });

  it('are created when missing', () => {
    expect(appendItem('a:\n  b: 1\n', ['a', 'needs'], 'x').text).toBe('a:\n  b: 1\n  needs: [x]\n');
    expect(appendItem('a: []\n', ['a'], 'x').text).toBe('a: [x]\n');
  });

  it('lose an item, inline or as lines', () => {
    expect(removeItem(handWritten, ['jobs', 'build', 'needs'], 0).text).toContain('    needs: []  # flow, unpadded\n');
    const block = removeItem(handWritten, ['jobs', 'build', 'steps'], 0);
    expect(changed(handWritten, block.text)).toEqual({ removed: ['    - run: "make"'], added: [] });
    expect(removeItem('a:\n  - x\nb: 1\n', ['a'], 0).text).toBe('a: []\nb: 1\n');
  });

  it('ignore indexes that are not there', () => {
    expect(removeItem('a: [1]\n', ['a'], 4).text).toBe('a: [1]\n');
    expect(removeItem('a: 1\n', ['a'], 0).text).toBe('a: 1\n');
  });
});

describe('scripts of several lines', () => {
  it('are written as a block under their key, and replaced as one', () => {
    const set = setIn(handWritten, ['jobs', 'build', 'steps', 0, 'run'], 'make\nmake test\n');
    expect(set.fallback).toBe(false);
    expect(set.text).toContain("    - run: |\n        make\n        make test\n    - run: 'make test'\n");
    const back = setIn(set.text, ['jobs', 'build', 'steps', 0, 'run'], 'make');
    expect(back.text).toBe(handWritten.replace('run: "make"', 'run: make'));
  });

  it('keep their last line without a newline', () => {
    const set = setIn('a:\n  run: x\n', ['a', 'run'], 'one\ntwo');
    expect(set.text).toBe('a:\n  run: |-\n    one\n    two\n');
  });
});

describe('list items by index', () => {
  it('are replaced in place, in a block list and in a flow list', () => {
    const block = setIn(handWritten, ['jobs', 'build', 'steps', 1], { run: 'make check' });
    expect(changed(handWritten, block.text)).toEqual({
      removed: ["    - run: 'make test'"],
      added: ['    - run: make check'],
    });
    const flow = setIn(handWritten, ['jobs', 'build', 'needs', 0], 'check');
    expect(flow.text).toContain('    needs: [check]  # flow, unpadded\n');
  });

  it('are inserted before an item, or appended past the end', () => {
    const first = insertItem(handWritten, ['jobs', 'build', 'steps'], 0, { run: 'setup' });
    expect(first.fallback).toBe(false);
    expect(first.text).toContain('    steps:\n    - run: setup\n    - run: "make"\n');
    expect(insertItem(handWritten, ['jobs', 'build', 'needs'], 0, 'setup').text).toContain('needs: [setup, lint]');
    expect(insertItem(handWritten, ['jobs', 'build', 'steps'], 9, { run: 'last' }).text).toContain(
      "    - run: 'make test'\n    - run: last\n",
    );
  });

  it('move with their lines and comments, up and down', () => {
    const text = 'steps:\n  # first\n  - run: a\n  - run: b\n    name: B   # bee\n  - run: c\n';
    const down = moveItem(text, ['steps'], 0, 2);
    expect(down).toEqual({
      text: 'steps:\n  # first\n  - run: b\n    name: B   # bee\n  - run: c\n  - run: a\n',
      fallback: false,
    });
    const up = moveItem(text, ['steps'], 1, 0);
    expect(up.text).toBe('steps:\n  # first\n  - run: b\n    name: B   # bee\n  - run: a\n  - run: c\n');
    expect(moveItem('a: [1, 2, 3]\n', ['a'], 2, 0).text).toBe('a: [3, 1, 2]\n');
    expect(moveItem('a: [1]\n', ['a'], 0, 3).text).toBe('a: [1]\n');
  });
});

describe('renaming a key', () => {
  it('changes only the key, keeping value, place and comments', () => {
    const renamed = renameKey(handWritten, ['jobs', 'lint'], 'check');
    expect(changed(handWritten, renamed.text)).toEqual({
      removed: ['  lint: {steps: [{run: lint}]}'],
      added: ['  check: {steps: [{run: lint}]}'],
    });
    expect(renameKey(handWritten, ['x-editor', 'layout', 'build'], 'compile').text).toContain('    compile: { x: 10');
  });

  it('refuses a key that is taken or a pair that is not there', () => {
    expect(renameKey(handWritten, ['jobs', 'lint'], 'build').text).toBe(handWritten);
    expect(renameKey(handWritten, ['jobs', 'nope'], 'x').text).toBe(handWritten);
  });
});
