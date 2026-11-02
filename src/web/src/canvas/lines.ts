/**
 * Which lines of the file each job takes, to go from a card to its YAML and back. Read from the
 * YAML document's own node ranges, so comments, blank lines and flow syntax do not confuse it.
 */
import { isMap, isScalar, LineCounter, parseDocument } from 'yaml';

/** First and last line (1-based) of a job's entry under `jobs`, from its key to its value's end. */
export interface LineRange {
  from: number;
  to: number;
}

export function jobLines(source: string): Map<string, LineRange> {
  const counter = new LineCounter();
  const document = parseDocument(source, { lineCounter: counter });
  const ranges = new Map<string, LineRange>();
  const root = document.contents;
  if (!isMap(root)) {
    return ranges;
  }
  const jobs = root.items.find((pair) => isScalar(pair.key) && pair.key.value === 'jobs')?.value;
  if (!isMap(jobs)) {
    return ranges;
  }
  for (const pair of jobs.items) {
    if (!isScalar(pair.key) || !pair.key.range) {
      continue;
    }
    const start = pair.key.range[0];
    const valueRange = (pair.value as { range?: [number, number, number] } | null)?.range;
    // The value ends where its last content ends; a trailing newline belongs to the next line.
    const end = Math.max(start, (valueRange?.[1] ?? pair.key.range[1]) - 1);
    ranges.set(String(pair.key.value), { from: counter.linePos(start).line, to: counter.linePos(end).line });
  }
  return ranges;
}

/** The job whose lines include `line`, if any. */
export function jobAtLine(ranges: ReadonlyMap<string, LineRange>, line: number): string | null {
  for (const [id, range] of ranges) {
    if (line >= range.from && line <= range.to) {
      return id;
    }
  }
  return null;
}
