/**
 * A line diff, for comparing two versions of an automation file.
 *
 * "Why does this automation do that now" is answered by what changed between two versions, not by
 * two walls of YAML side by side. Written here rather than pulled in: it is one algorithm over a
 * few hundred lines of text, and the useful part is what gets left out of it (`withoutLayout`).
 */

export type DiffKind = 'same' | 'added' | 'removed';

export interface DiffLine {
  kind: DiffKind;
  text: string;
  /** Line number in the older text, or null for an added line. */
  before: number | null;
  /** Line number in the newer text, or null for a removed line. */
  after: number | null;
}

/**
 * The longest common subsequence of two line arrays, as a table of lengths. O(n·m) in time and
 * memory, which for two automation files (a few hundred lines at most) is some tens of thousands
 * of numbers.
 */
function lcsTable(a: readonly string[], b: readonly string[]): Uint32Array {
  const width = b.length + 1;
  const table = new Uint32Array((a.length + 1) * width);
  const at = (i: number, j: number) => table[i * width + j] ?? 0;

  for (let i = a.length - 1; i >= 0; i -= 1) {
    for (let j = b.length - 1; j >= 0; j -= 1) {
      table[i * width + j] = a[i] === b[j] ? at(i + 1, j + 1) + 1 : Math.max(at(i + 1, j), at(i, j + 1));
    }
  }

  return table;
}

/** Every line of both texts, in order, marked as kept, added or removed. */
export function lineDiff(before: string, after: string): DiffLine[] {
  const a = before.split('\n');
  const b = after.split('\n');
  const width = b.length + 1;
  const table = lcsTable(a, b);
  const at = (i: number, j: number) => table[i * width + j] ?? 0;

  const lines: DiffLine[] = [];
  let i = 0;
  let j = 0;

  while (i < a.length && j < b.length) {
    const left = a[i] ?? '';
    const right = b[j] ?? '';
    if (left === right) {
      lines.push({ kind: 'same', text: left, before: i + 1, after: j + 1 });
      i += 1;
      j += 1;
    } else if (at(i + 1, j) >= at(i, j + 1)) {
      lines.push({ kind: 'removed', text: left, before: i + 1, after: null });
      i += 1;
    } else {
      lines.push({ kind: 'added', text: right, before: null, after: j + 1 });
      j += 1;
    }
  }

  for (; i < a.length; i += 1) {
    lines.push({ kind: 'removed', text: a[i] ?? '', before: i + 1, after: null });
  }
  for (; j < b.length; j += 1) {
    lines.push({ kind: 'added', text: b[j] ?? '', before: null, after: j + 1 });
  }

  return lines;
}

/** A group of shown lines, after `skipped` unchanged lines that are left out. */
export interface DiffHunk {
  skipped: number;
  lines: DiffLine[];
}

/**
 * Drops long runs of unchanged lines, leaving `context` lines around each change. A file is mostly
 * unchanged between two versions, so an unabridged diff buries the three lines that moved. Each
 * left-out run becomes a gap the reader can see is a gap; unchanged lines after the last change end
 * in a hunk without lines.
 */
export function collapse(lines: readonly DiffLine[], context = 3): DiffHunk[] {
  const changed = lines.map((line) => line.kind !== 'same');
  const shown = lines.map((_, index) => changed.slice(Math.max(0, index - context), index + context + 1).some(Boolean));

  const hunks: DiffHunk[] = [];
  let skipped = 0;
  lines.forEach((line, index) => {
    if (!shown[index]) {
      skipped += 1;
      return;
    }
    const last = hunks.at(-1);
    if (skipped > 0 || !last) {
      hunks.push({ skipped, lines: [line] });
      skipped = 0;
    } else {
      last.lines.push(line);
    }
  });

  if (skipped > 0 && hunks.length > 0) {
    hunks.push({ skipped, lines: [] });
  }
  return hunks;
}

/**
 * The file without its canvas layout. `x-editor` holds where each node sits on the canvas; moving a
 * job a few pixels rewrites every coordinate under it, so a diff that includes it reports a redesign
 * whenever somebody tidied the canvas. It is left out of the comparison, and the caller says so.
 */
export function withoutLayout(yaml: string): { text: string; removed: boolean } {
  const lines = yaml.split('\n');
  const start = lines.findIndex((line) => line.startsWith('x-editor:'));
  if (start === -1) {
    return { text: yaml, removed: false };
  }

  // The block runs until the next line that starts at column zero: the next top-level key.
  let end = start + 1;
  while (end < lines.length && ((lines[end] ?? '').trim() === '' || /^\s/.test(lines[end] ?? ''))) {
    end += 1;
  }

  return { text: [...lines.slice(0, start), ...lines.slice(end)].join('\n'), removed: true };
}

const lineCount = (n: number, what: string) => `${n} ${n === 1 ? 'line' : 'lines'} ${what}`;

/** How much the file changed, in words; a change to the canvas layout alone is named as such. */
export function changeSummary(before: string, after: string): string {
  if (withoutLayout(before).text === withoutLayout(after).text) {
    return 'Only the places of cards on the canvas changed.';
  }
  const lines = lineDiff(before, after);
  const added = lines.filter((line) => line.kind === 'added').length;
  const removed = lines.filter((line) => line.kind === 'removed').length;
  const parts = [added > 0 ? lineCount(added, 'added') : '', removed > 0 ? lineCount(removed, 'removed') : ''];
  return `${parts.filter((part) => part !== '').join(', ')}.`;
}
