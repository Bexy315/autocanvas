import { describe, expect, it } from 'vitest';
import { appendItem, deleteIn, removeItem, setIn } from './yamlEdit';

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
