/**
 * Colors and emphasis from ANSI escape sequences in log lines, as text segments with a style.
 * Only SGR sequences (`ESC [ … m`) are understood: the 16 basic colors (drawn with the theme's log
 * colors, readable in light and dark), the 256-color palette, RGB, bold, dim, italic, underline and
 * their resets. Every other control sequence is removed. Nothing here produces HTML: the result is
 * data that the log turns into React elements, so a log can never inject markup.
 */

export interface AnsiStyle {
  /** One of the 16 basic colors (0-15), drawn with the theme's `--log-N` color. */
  fg?: number;
  bg?: number;
  /** A color outside the 16, as CSS. */
  fgCss?: string;
  bgCss?: string;
  bold?: boolean;
  dim?: boolean;
  italic?: boolean;
  underline?: boolean;
}

export interface AnsiSegment {
  text: string;
  style: AnsiStyle;
}

// Control sequences: CSI (`ESC [` … final byte), OSC (`ESC ]` … BEL or `ESC \`), and two-byte escapes.
// Escape characters are what this pattern is about.
// oxlint-disable-next-line no-control-regex -- see above
const escapePattern = /\u001B(?:\[([0-?]*)[ -/]*([@-~])|\][^\u0007\u001B]*(?:\u0007|\u001B\\)|[@-Z\\-_])/g;

const cubeLevel = (value: number) => (value === 0 ? 0 : 55 + value * 40);

/** The 256-color palette beyond the first 16: a 6×6×6 cube, then 24 greys. */
function paletteCss(index: number): string {
  if (index >= 232) {
    const level = 8 + (index - 232) * 10;
    return `rgb(${level}, ${level}, ${level})`;
  }
  const cube = index - 16;
  return `rgb(${cubeLevel(Math.floor(cube / 36))}, ${cubeLevel(Math.floor(cube / 6) % 6)}, ${cubeLevel(cube % 6)})`;
}

/** Reads an extended color (`38;5;N` or `38;2;R;G;B`) from `codes` at `at`; how many codes it took. */
function extended(codes: readonly number[], at: number): { basic?: number; css?: string; used: number } {
  const mode = codes[at + 1];
  if (mode === 5) {
    const index = codes[at + 2] ?? 0;
    return index < 16 ? { basic: index, used: 3 } : { css: paletteCss(Math.min(index, 255)), used: 3 };
  }
  if (mode === 2) {
    const [r = 0, g = 0, b = 0] = codes.slice(at + 2, at + 5).map((value) => Math.min(value, 255));
    return { css: `rgb(${r}, ${g}, ${b})`, used: 5 };
  }
  return { used: 1 };
}

function withoutColor(style: AnsiStyle, layer: 'fg' | 'bg'): AnsiStyle {
  const next = { ...style };
  delete next[layer];
  delete next[layer === 'fg' ? 'fgCss' : 'bgCss'];
  return next;
}

/** The style after an SGR sequence's codes. */
export function applyCodes(style: AnsiStyle, codes: readonly number[]): AnsiStyle {
  let next = style;
  for (let at = 0; at < codes.length; at += 1) {
    const code = codes[at] ?? 0;
    if (code === 0) {
      next = {};
    } else if (code === 1) {
      next = { ...next, bold: true };
    } else if (code === 2) {
      next = { ...next, dim: true };
    } else if (code === 3) {
      next = { ...next, italic: true };
    } else if (code === 4) {
      next = { ...next, underline: true };
    } else if (code === 22) {
      next = { ...next, bold: false, dim: false };
    } else if (code === 23) {
      next = { ...next, italic: false };
    } else if (code === 24) {
      next = { ...next, underline: false };
    } else if ((code >= 30 && code <= 37) || (code >= 90 && code <= 97)) {
      next = { ...withoutColor(next, 'fg'), fg: code >= 90 ? code - 82 : code - 30 };
    } else if ((code >= 40 && code <= 47) || (code >= 100 && code <= 107)) {
      next = { ...withoutColor(next, 'bg'), bg: code >= 100 ? code - 92 : code - 40 };
    } else if (code === 39 || code === 49) {
      next = withoutColor(next, code === 39 ? 'fg' : 'bg');
    } else if (code === 38 || code === 48) {
      const layer = code === 38 ? 'fg' : 'bg';
      const color = extended(codes, at);
      const cleared = withoutColor(next, layer);
      next =
        color.basic !== undefined
          ? { ...cleared, [layer]: color.basic }
          : color.css
            ? { ...cleared, [layer === 'fg' ? 'fgCss' : 'bgCss']: color.css }
            : cleared;
      at += color.used - 1;
    }
  }
  return next;
}

/**
 * What a terminal would show of a line that redraws itself: text after the last carriage return
 * (a progress bar written over and over), unless that return ends the line.
 */
function lastRedraw(text: string): string {
  const trimmed = text.replace(/\r+$/, '');
  const at = trimmed.lastIndexOf('\r');
  return at === -1 ? trimmed : trimmed.slice(at + 1);
}

/** A log line as segments of text, each with the style the escape sequences before it set. */
export function parseAnsi(line: string, start: AnsiStyle = {}): AnsiSegment[] {
  const text = lastRedraw(line);
  const segments: AnsiSegment[] = [];
  let style = start;
  let from = 0;
  const push = (piece: string) => {
    if (piece === '') {
      return;
    }
    const last = segments.at(-1);
    if (last && last.style === style) {
      last.text += piece;
    } else {
      segments.push({ text: piece, style });
    }
  };
  for (const match of text.matchAll(escapePattern)) {
    push(text.slice(from, match.index));
    from = match.index + match[0].length;
    if (match[2] === 'm') {
      const codes = (match[1] ?? '').split(/[;:]/).map((code) => (code === '' ? 0 : Number(code)));
      style = applyCodes(style, codes);
    }
  }
  push(text.slice(from));
  return segments;
}

/** The line's text without any escape sequences: what search and copy work on. */
export function plainText(line: string): string {
  return lastRedraw(line).replaceAll(escapePattern, '');
}
