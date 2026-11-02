/**
 * Where the cursor is in an automation file, read from indentation rather than a parse, so it works
 * on the half-typed line a completion is asked for.
 */

/** A list item in a path; other segments are mapping keys. */
export const item = '[]';

export interface CursorPlace {
  /** The keys and list items above the cursor, from the top: `['jobs', 'build', 'steps', '[]', 'with']`. */
  path: string[];
  /** Typing a key, or the value of `key`. */
  mode: 'key' | 'value';
  /** The key whose value is typed (mode `value`). */
  key?: string;
  /** What is typed so far of the key or value. */
  prefix: string;
  /** Offset where the typed prefix starts; a completion replaces from here to the cursor. */
  from: number;
  /** The other keys already in the same mapping, so they are not offered again. */
  siblings: string[];
  /** For a list item: the values of its keys written on its own lines, such as `type`. */
  itemValues: Record<string, string>;
}

interface Line {
  text: string;
  start: number;
}

function lines(text: string): Line[] {
  const result: Line[] = [];
  let start = 0;
  for (const line of text.split('\n')) {
    result.push({ text: line, start });
    start += line.length + 1;
  }
  return result;
}

const keyLine = /^(\s*)(- +)?([A-Za-z0-9_-]+)\s*:(?:\s|$)/;
const dashLine = /^(\s*)- */;

function isBlank(text: string): boolean {
  const trimmed = text.trim();
  return trimmed === '' || trimmed.startsWith('#');
}

/** The column where the line's first key starts, and the column of its list dash, if any. */
function columns(text: string): { key: number; dash: number | null; name: string | null; value: string } {
  const match = keyLine.exec(text);
  if (match) {
    const indent = match[1]?.length ?? 0;
    const dash = match[2] ? indent : null;
    const key = indent + (match[2]?.length ?? 0);
    return { key, dash, name: match[3] ?? null, value: text.slice(match[0].length).trim() };
  }
  const dashed = dashLine.exec(text);
  if (dashed && text.trimStart().startsWith('-')) {
    const indent = dashed[1]?.length ?? 0;
    return { key: dashed[0].length, dash: indent, name: null, value: '' };
  }
  return { key: text.length - text.trimStart().length, dash: null, name: null, value: '' };
}

/** The place of the cursor at `offset` in `text`, or null where nothing can be completed (comments, block text). */
export function placeAt(text: string, offset: number): CursorPlace | null {
  const all = lines(text);
  const lineIndex = all.findIndex((line, index) => offset <= line.start + line.text.length || index === all.length - 1);
  const current = all[lineIndex];
  if (!current) {
    return null;
  }
  const before = current.text.slice(0, offset - current.start);
  if (before.trimStart().startsWith('#')) {
    return null;
  }

  const valueMatch =
    /^(\s*)(- +)?([A-Za-z0-9_-]+)\s*:\s+([^\s#]*)$/.exec(before) ?? /^(\s*)(- +)?([A-Za-z0-9_-]+):()$/.exec(before);
  const keyMatch = /^(\s*)(- +)?([A-Za-z0-9_-]*)$/.exec(before);
  let mode: 'key' | 'value';
  let keyColumn: number;
  let prefix: string;
  let key: string | undefined;
  let inItem: number | null = null;
  if (valueMatch) {
    mode = 'value';
    key = valueMatch[3] ?? '';
    prefix = valueMatch[4] ?? '';
    keyColumn = (valueMatch[1]?.length ?? 0) + (valueMatch[2]?.length ?? 0);
    inItem = valueMatch[2] ? (valueMatch[1]?.length ?? 0) : null;
  } else if (keyMatch) {
    mode = 'key';
    prefix = keyMatch[3] ?? '';
    keyColumn = (keyMatch[1]?.length ?? 0) + (keyMatch[2]?.length ?? 0);
    inItem = keyMatch[2] ? (keyMatch[1]?.length ?? 0) : null;
  } else {
    return null;
  }

  // Walk upwards: each line indented less than everything seen so far is a parent.
  const path: string[] = [];
  let threshold = keyColumn;
  let itemStart: number | null = inItem === null ? null : lineIndex;
  let itemKeyColumn = inItem === null ? null : keyColumn;
  if (inItem !== null) {
    path.unshift(item);
    threshold = inItem;
  }
  const siblingLines: number[] = [];
  for (let index = lineIndex - 1; index >= 0; index -= 1) {
    // Above the top-level parent there is nothing more to find; at the top level, siblings go on.
    if (threshold === 0 && path.length > 0) {
      break;
    }
    const content = all[index]?.text ?? '';
    if (isBlank(content)) {
      continue;
    }
    const line = columns(content);
    if (line.dash !== null && line.dash < threshold) {
      if (itemStart === null) {
        itemStart = index;
        itemKeyColumn = line.key;
      }
      if (line.key === keyColumn && path.length === 0) {
        siblingLines.push(index);
      }
      path.unshift(item);
      threshold = line.dash;
      continue;
    }
    if (line.key < threshold && line.name) {
      path.unshift(line.name);
      threshold = line.key;
      continue;
    }
    if (line.key === keyColumn && path.length === 0) {
      siblingLines.push(index);
    }
  }

  // Siblings after the cursor, until the mapping ends.
  for (let index = lineIndex + 1; index < all.length; index += 1) {
    const content = all[index]?.text ?? '';
    if (isBlank(content)) {
      continue;
    }
    const line = columns(content);
    if (line.key < keyColumn || (line.dash !== null && line.dash < keyColumn)) {
      break;
    }
    if (line.key === keyColumn) {
      siblingLines.push(index);
    }
  }
  const siblings = siblingLines.flatMap((index) => {
    const name = columns(all[index]?.text ?? '').name;
    return name ? [name] : [];
  });

  // The keys of the innermost list item, for context such as a step's `type`.
  const itemValues: Record<string, string> = {};
  if (itemStart !== null && itemKeyColumn !== null) {
    for (let index = itemStart; index < all.length; index += 1) {
      const content = all[index]?.text ?? '';
      if (isBlank(content)) {
        continue;
      }
      const line = columns(content);
      if (index > itemStart && (line.key < itemKeyColumn || (line.dash !== null && line.dash < itemKeyColumn))) {
        break;
      }
      if (line.key === itemKeyColumn && line.name && index !== lineIndex) {
        itemValues[line.name] = line.value.replace(/^["']|["']$/g, '');
      }
    }
  }

  return {
    path,
    mode,
    ...(key === undefined ? {} : { key }),
    prefix,
    from: offset - prefix.length,
    siblings,
    itemValues,
  };
}
