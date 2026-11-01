import { describe, expect, it } from 'vitest';
import { collapse, lineDiff, withoutLayout } from './diff';

function rendered(before: string, after: string): string[] {
  const marks = { added: '+', removed: '-', same: ' ' } as const;
  return lineDiff(before, after).map((line) => `${marks[line.kind]}${line.text}`);
}

describe('line diffs', () => {
  it('report nothing for identical text', () => {
    const lines = lineDiff('a\nb\nc', 'a\nb\nc');
    expect(lines.every((line) => line.kind === 'same')).toBe(true);
    expect(lines).toHaveLength(3);
  });

  it('find a changed line as a removal and an addition', () => {
    expect(rendered('a\nb\nc', 'a\nB\nc')).toEqual([' a', '-b', '+B', ' c']);
  });

  it('find an inserted line without rewriting the rest', () => {
    expect(rendered('a\nc', 'a\nb\nc')).toEqual([' a', '+b', ' c']);
  });

  it('find a deleted line', () => {
    expect(rendered('a\nb\nc', 'a\nc')).toEqual([' a', '-b', ' c']);
  });

  it('number lines on the side they exist', () => {
    const lines = lineDiff('keep\nold', 'keep\nnew');
    expect(lines.map((line) => [line.kind, line.before, line.after])).toEqual([
      ['same', 1, 1],
      ['removed', 2, null],
      ['added', null, 2],
    ]);
  });

  it('handle an empty side', () => {
    expect(rendered('', 'a')).toEqual(['-', '+a']);
    expect(lineDiff('a\nb', '').map((line) => line.kind)).toEqual(['removed', 'removed', 'added']);
  });

  it('report a moved line as one removal and one addition, not a rewrite', () => {
    const lines = lineDiff('x\na\nb\nc', 'a\nb\nc\nx');
    expect(lines.filter((line) => line.kind === 'same').map((line) => line.text)).toEqual(['a', 'b', 'c']);
    expect(lines.filter((line) => line.kind !== 'same').map((line) => line.text)).toEqual(['x', 'x']);
  });
});

describe('collapsing unchanged lines', () => {
  const long = Array.from({ length: 40 }, (_, index) => `line ${index}`).join('\n');
  const changed = long.replace('line 20', 'line twenty');

  it('leaves out the unchanged middle and end, and says how much', () => {
    const hunks = collapse(lineDiff(long, changed));
    expect(hunks.map((hunk) => [hunk.skipped, hunk.lines.length])).toEqual([
      [17, 8],
      [16, 0],
    ]);
  });

  it('makes one hunk per group of changes', () => {
    const twice = long.replace('line 2\n', 'line two\n').replace('line 30', 'line thirty');
    const hunks = collapse(lineDiff(long, twice)).filter((hunk) => hunk.lines.length > 0);
    expect(hunks).toHaveLength(2);
    expect(hunks[1]?.skipped).toBeGreaterThan(0);
  });

  it('shows everything when everything is near a change', () => {
    expect(collapse(lineDiff('a\nb', 'a\nB'))).toEqual([{ skipped: 0, lines: lineDiff('a\nb', 'a\nB') }]);
  });

  it('shows nothing for identical text', () => {
    expect(collapse(lineDiff('a\nb', 'a\nb'))).toEqual([]);
  });
});

describe('leaving out the canvas layout', () => {
  const withLayout =
    'name: thing\njobs:\n  work:\n    steps: []\nx-editor:\n  layout:\n    work:\n      x: 10\n      y: 20\n';

  it('removes the layout block and says it did', () => {
    const { text, removed } = withoutLayout(withLayout);
    expect(removed).toBe(true);
    expect(text).not.toContain('x-editor');
    expect(text).not.toContain('x: 10');
    expect(text).toContain('jobs:');
  });

  it('leaves text without a layout block alone', () => {
    expect(withoutLayout('name: thing\njobs: {}\n')).toEqual({ text: 'name: thing\njobs: {}\n', removed: false });
  });

  it('stops at the next top-level key', () => {
    const { text } = withoutLayout(
      'name: thing\nx-editor:\n  layout:\n    work: { x: 1, y: 2 }\nconcurrency: single\n',
    );
    expect(text).toContain('concurrency: single');
    expect(text).not.toContain('layout');
  });
});
