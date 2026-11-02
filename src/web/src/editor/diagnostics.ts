import type { Diagnostic as EditorDiagnostic } from '@codemirror/lint';
import type { Text } from '@codemirror/state';
import type { Diagnostic } from '../api/problem';

/** The editor offset of a diagnostic's line and column (1-based); out-of-range values are clamped. */
export function offsetOf(
  doc: Text,
  line: number | string | null | undefined,
  column: number | string | null | undefined,
): number {
  if (line === null || line === undefined) {
    return 0;
  }
  const lineNumber = Math.min(Math.max(1, Number(line)), doc.lines);
  const target = doc.line(lineNumber);
  const columnNumber = column === null || column === undefined ? 1 : Math.max(1, Number(column));
  return Math.min(target.from + columnNumber - 1, target.to);
}

/** How far a marker reaches: to the end of the word at `from`, at least one character, within the line. */
function endOf(doc: Text, from: number): number {
  const line = doc.lineAt(from);
  const rest = line.text.slice(from - line.from);
  const word = /^[^\s,\]}]+/.exec(rest)?.[0].length ?? 0;
  if (word > 0) {
    return from + word;
  }
  return line.to > from ? from + 1 : from;
}

/** The server's diagnostics as editor markers, each with its code. */
export function toEditorDiagnostics(doc: Text, diagnostics: readonly Diagnostic[]): EditorDiagnostic[] {
  return diagnostics.map((diagnostic) => {
    const from = offsetOf(doc, diagnostic.line, diagnostic.column);
    return {
      from,
      to: endOf(doc, from),
      severity: diagnostic.severity === 'Error' ? 'error' : 'warning',
      message: diagnostic.message,
      source: diagnostic.code,
    };
  });
}
