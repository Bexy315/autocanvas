import { describe, expect, it } from 'vitest';
import type { JobStep, LogChunk } from '../../api/runs';
import { applyCodes, parseAnsi, plainText } from './ansi';
import { groupLines, lastSequence, logRows, logText, mergeChunks, numberLines, searchLines } from './logModel';

const esc = '\u001B';

describe('ANSI colors', () => {
  it.each([
    ['plain text', 'hello', [{ text: 'hello', style: {} }]],
    [
      'a basic color and its reset',
      `${esc}[31mred${esc}[0m plain`,
      [
        { text: 'red', style: { fg: 1 } },
        { text: ' plain', style: {} },
      ],
    ],
    ['bright colors', `${esc}[92mok`, [{ text: 'ok', style: { fg: 10 } }]],
    ['a background', `${esc}[41;97m FAIL `, [{ text: ' FAIL ', style: { bg: 1, fg: 15 } }]],
    [
      'bold, then not',
      `${esc}[1mbig${esc}[22msmall`,
      [
        { text: 'big', style: { bold: true } },
        { text: 'small', style: { bold: false, dim: false } },
      ],
    ],
    ['256 colors', `${esc}[38;5;196mx`, [{ text: 'x', style: { fgCss: 'rgb(255, 0, 0)' } }]],
    ['256 greys', `${esc}[48;5;240mx`, [{ text: 'x', style: { bgCss: 'rgb(88, 88, 88)' } }]],
    ['the first 16 of 256', `${esc}[38;5;2mx`, [{ text: 'x', style: { fg: 2 } }]],
    ['RGB', `${esc}[38;2;10;20;300mx`, [{ text: 'x', style: { fgCss: 'rgb(10, 20, 255)' } }]],
    [
      'an empty reset',
      `${esc}[31ma${esc}[mb`,
      [
        { text: 'a', style: { fg: 1 } },
        { text: 'b', style: {} },
      ],
    ],
    ['other sequences, removed', `${esc}[2K${esc}[1Gdone${esc}]0;title\u0007!`, [{ text: 'done!', style: {} }]],
    ['a redrawn line, as shown last', '10%\r50%\r100%', [{ text: '100%', style: {} }]],
  ])('%s', (_, line, segments) => {
    expect(parseAnsi(line)).toEqual(segments);
  });

  it('drops a color for the default one, and knows italic, underline and dim', () => {
    expect(applyCodes({ fg: 1, bg: 2 }, [39, 49])).toEqual({});
    expect(applyCodes({}, [2, 3, 4])).toEqual({ dim: true, italic: true, underline: true });
    expect(applyCodes({ italic: true, underline: true }, [23, 24])).toEqual({ italic: false, underline: false });
    expect(applyCodes({}, [38, 9])).toEqual({});
  });

  it('gives search the text without sequences', () => {
    expect(plainText(`${esc}[1;31merror${esc}[0m: x\r`)).toBe('error: x');
  });
});

const line = (text: string, source: 'Stdout' | 'Stderr' | 'System' = 'Stdout') => ({
  time: '2026-11-02T10:00:00Z',
  source,
  text,
});

const chunk = (sequence: number, stepIndex: number | null, ...texts: string[]): LogChunk => ({
  sequence,
  stepIndex,
  lines: texts.map((text) => line(text)),
});

const steps = [
  { index: 0, name: 'Install' },
  { index: 1, name: 'Test' },
] as unknown as JobStep[];

describe('the log model', () => {
  it('keeps each chunk once, in order, and numbers the lines', () => {
    const known = mergeChunks(new Map(), [chunk(2, 0, 'b'), chunk(1, null, 'a')]);
    const again = mergeChunks(known, [chunk(2, 0, 'b'), chunk(3, 1, 'c', 'd')]);
    const lines = numberLines(again);
    expect(lines.map((entry) => [entry.number, entry.text, entry.stepIndex])).toEqual([
      [1, 'a', null],
      [2, 'b', 0],
      [3, 'c', 1],
      [4, 'd', 1],
    ]);
    expect(lastSequence(again)).toBe(3);
    expect(lastSequence(new Map())).toBeNull();
  });

  it('groups lines by step, names the runner before and after, and folds groups away', () => {
    const lines = numberLines(
      mergeChunks(new Map(), [chunk(1, null, 'pull'), chunk(2, 0, 'i'), chunk(3, 1, 't'), chunk(4, null, 'clean')]),
    );
    const groups = groupLines(lines, steps);
    expect(groups.map((group) => group.title)).toEqual(['Preparing', '1. Install', '2. Test', 'Cleaning up']);
    const rows = logRows(groups, new Set(['step-0']));
    expect(rows.map((row) => (row.kind === 'group' ? `[${row.group.title}]` : row.line.text))).toEqual([
      '[Preparing]',
      'pull',
      '[1. Install]',
      '[2. Test]',
      't',
      '[Cleaning up]',
      'clean',
    ]);
    expect(groupLines(numberLines(mergeChunks(new Map(), [chunk(1, 5, 'x')])), steps)[0]?.title).toBe('6. Step 6');
  });

  it('finds lines ignoring case and colors, and writes the log as text', () => {
    const lines = numberLines(
      mergeChunks(new Map(), [chunk(1, 0, `${esc}[31mError${esc}[0m here`, 'fine', 'an error')]),
    );
    expect(searchLines(lines, 'ERROR')).toEqual([1, 3]);
    expect(searchLines(lines, '  ')).toEqual([]);
    expect(logText(lines)).toBe(
      '2026-11-02T10:00:00Z   Error here\n2026-11-02T10:00:00Z   fine\n2026-11-02T10:00:00Z   an error',
    );
  });
});
