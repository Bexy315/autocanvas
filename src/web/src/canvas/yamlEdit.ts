/**
 * Changes to an automation file that touch only what changes.
 *
 * The YAML is the source of truth (ADR-0018), and people write it by hand: comments, blank lines,
 * quotes, flow lists and their own indentation must survive an edit made on the canvas. Writing the
 * whole document back (`Document.toString()`) normalises all of that, so these functions splice the
 * text instead: they find the node's range in the parsed document and replace, insert or remove only
 * those characters, rendering new values in the file's own style.
 *
 * Every result is read back and compared with the same change made on the parsed document. If the
 * splice did not produce exactly that content, the whole document is written instead: correct
 * content always wins over keeping the formatting.
 */
import {
  Document,
  isMap,
  isScalar,
  isSeq,
  parseDocument,
  type Node as YamlNode,
  type Pair,
  type YAMLMap,
  type YAMLSeq,
} from 'yaml';

export type Path = readonly (string | number)[];

export interface EditResult {
  text: string;
  /** Whether the splice failed its check and the whole document was written instead. */
  fallback: boolean;
}

export interface ValueOptions {
  /** Write a list or mapping in flow style, `[a, b]` or `{ x: 1 }`, instead of as a block. */
  flow?: boolean;
  /** A new key goes before the mapping's first one instead of after its last, as `needs` is written. */
  first?: boolean;
}

type Range = [number, number, number];
type Collection = YAMLMap | YAMLSeq;

const renderOptions = { lineWidth: 0, flowCollectionPadding: false } as const;

function rangeOf(node: unknown): Range | null {
  return (node as { range?: Range | null } | null)?.range ?? null;
}

function columnOf(text: string, offset: number): number {
  return offset - (text.lastIndexOf('\n', offset - 1) + 1);
}

function lineStart(text: string, offset: number): number {
  return text.lastIndexOf('\n', offset - 1) + 1;
}

/** The offset after the newline that ends the line containing `offset` (or the end of the text). */
function lineEnd(text: string, offset: number): number {
  const newline = text.indexOf('\n', offset);
  return newline === -1 ? text.length : newline + 1;
}

/** Where a node's content ends: block collections' ranges also cover the newlines after them. */
function contentEnd(text: string, node: unknown): number {
  const range = rangeOf(node);
  if (!range) {
    return 0;
  }
  let end = range[1];
  while (end > range[0] && /\s/.test(text[end - 1] ?? '')) {
    end -= 1;
  }
  return end;
}

/** Whether the file writes block lists under their key without indenting them (`steps:\n- run`). */
function sequencesIndented(text: string): boolean {
  return !/^([ \t]*)[^\s#][^\n]*:[ \t]*\n\1- /m.test(text);
}

function isCollectionValue(value: unknown): boolean {
  if (isMap(value) || isSeq(value)) {
    return !value.flow;
  }
  return typeof value === 'object' && value !== null && !isScalar(value);
}

/**
 * `value` as a node below the keys `rest`, with the innermost value in flow style when asked: a
 * position created with its `x-editor` and `layout` parents stays `{x: 1, y: 2}`.
 */
function nestedNode(value: unknown, rest: Path, options: ValueOptions): YamlNode {
  const document = new Document();
  const leaf = document.createNode(value);
  if (options.flow) {
    makeFlow(leaf);
  }
  return rest.reduceRight<YamlNode>((inner, segment) => {
    const map = document.createNode({}) as YAMLMap;
    map.set(segment, inner);
    return map;
  }, leaf as YamlNode);
}

/** A value on one line: a scalar, or a collection in flow style without padding. */
function renderInline(value: unknown): string {
  const document = new Document(value);
  if (isMap(document.contents) || isSeq(document.contents)) {
    makeFlow(document.contents);
  }
  return document.toString(renderOptions).replace(/\n$/, '');
}

function makeFlow(node: unknown): void {
  if (isMap(node) || isSeq(node)) {
    node.flow = true;
  }
}

/** A block collection's lines, each prefixed with `indent` spaces. */
function renderBlock(value: unknown, indent: number, indentSeq: boolean): string {
  const rendered = new Document(value).toString({ ...renderOptions, indentSeq });
  return rendered
    .replace(/\n$/, '')
    .split('\n')
    .map((line) => (line === '' ? line : ' '.repeat(indent) + line))
    .join('\n');
}

/** `key: value` or `key:` plus a block below it, with the key at column `indent`. */
function renderPair(key: string, value: unknown, indent: number, options: ValueOptions, indentSeq: boolean): string {
  const name = renderInline(key);
  if (!isCollectionValue(value) || options.flow) {
    return `${' '.repeat(indent)}${name}: ${renderInline(value)}`;
  }
  return `${' '.repeat(indent)}${name}:\n${renderBlock(value, indent + 2, indentSeq)}`;
}

function splice(text: string, from: number, to: number, insert: string): string {
  return text.slice(0, from) + insert + text.slice(to);
}

function valueNode(value: unknown, options: ValueOptions, document: Document): YamlNode {
  const node = document.createNode(value);
  if (options.flow) {
    makeFlow(node);
  }
  return node as YamlNode;
}

/** The parsed file; the canvas only edits files it can read. */
function readable(text: string): Document {
  const document = parseDocument(text);
  if (document.errors.length > 0) {
    throw new Error('The file cannot be read, so it cannot be changed from the canvas.');
  }
  return document;
}

/** JSON with mapping keys in a fixed order: YAML mappings have no order, so a key put first still matches. */
function canonical(value: unknown): string {
  if (Array.isArray(value)) {
    return `[${value.map(canonical).join(',')}]`;
  }
  if (typeof value === 'object' && value !== null) {
    const entries = Object.entries(value).toSorted(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0));
    return `{${entries.map(([key, inner]) => `${JSON.stringify(key)}:${canonical(inner)}`).join(',')}}`;
  }
  return JSON.stringify(value) ?? 'undefined';
}

/** Same content, ignoring formatting and the order of mapping keys. */
function sameContent(a: string, b: Document): boolean {
  const read = parseDocument(a);
  return read.errors.length === 0 && canonical(read.toJS()) === canonical(b.toJS());
}

/** Checks a splice against the expected document, writing the whole document when it differs. */
function verified(spliced: string | null, expected: Document, indentSeq: boolean): EditResult {
  if (spliced !== null && sameContent(spliced, expected)) {
    return { text: spliced, fallback: false };
  }
  return { text: expected.toString({ ...renderOptions, indentSeq }), fallback: true };
}

/** The pair under `key` in a map node, if any. */
function pairOf(map: unknown, key: string | number): Pair | undefined {
  if (!isMap(map)) {
    return undefined;
  }
  return map.items.find((pair) => isScalar(pair.key) && pair.key.value === key) as Pair | undefined;
}

/** Replaces a pair's value, keeping the key and anything after the value (such as a comment). */
function replacePairValue(
  text: string,
  pair: Pair,
  value: unknown,
  options: ValueOptions,
  indentSeq: boolean,
): string | null {
  const keyRange = rangeOf(pair.key);
  if (!keyRange) {
    return null;
  }
  const colon = text.indexOf(':', keyRange[1]);
  if (colon === -1) {
    return null;
  }
  const old = pair.value;
  const oldIsBlock = (isMap(old) || isSeq(old)) && !old.flow && old.items.length > 0;
  const end = old && rangeOf(old) ? (oldIsBlock ? contentEnd(text, old) : (rangeOf(old)?.[1] ?? colon + 1)) : colon + 1;
  if (!isCollectionValue(value) || options.flow) {
    return splice(text, colon + 1, end, ` ${renderInline(value)}`);
  }
  const indent = columnOf(text, keyRange[0]) + 2;
  return splice(text, colon + 1, end, `\n${renderBlock(value, indent, indentSeq)}`);
}

/** Sets the value at `path`, creating missing mappings on the way. */
export function setIn(text: string, path: Path, value: unknown, options: ValueOptions = {}): EditResult {
  const document = readable(text);
  const indentSeq = sequencesIndented(text);
  const expected = document.clone();
  expected.setIn([...path], valueNode(value, options, expected));
  if (path.length === 0) {
    return verified(null, expected, indentSeq);
  }
  return verified(spliceSet(text, document, path, value, options, indentSeq), expected, indentSeq);
}

function spliceSet(
  text: string,
  document: Document,
  path: Path,
  value: unknown,
  options: ValueOptions,
  indentSeq: boolean,
): string | null {
  // The deepest mapping on the path that exists; what is below it is created.
  let depth = path.length - 1;
  while (depth > 0 && !isMap(document.getIn(path.slice(0, depth), true))) {
    depth -= 1;
  }
  const parentPath = path.slice(0, depth);
  const parent = depth === 0 ? document.contents : document.getIn(parentPath, true);
  const key = path[depth];
  if (typeof key !== 'string') {
    return null;
  }
  const rest = path.slice(depth + 1);
  const nested = nestedNode(value, rest, options);
  const nestedOptions = { flow: !isCollectionValue(nested) };

  const existing = pairOf(parent, key);
  if (existing && rest.length === 0) {
    return replacePairValue(text, existing, nested, nestedOptions, indentSeq);
  }
  if (existing) {
    // A value is there but is not a mapping (e.g. `layout:` left empty): replace it as a whole.
    return replacePairValue(text, existing, nested, nestedOptions, indentSeq);
  }

  if (parent === null || parent === undefined) {
    // An empty file: the pair is all there is.
    const prefix = text.trim() === '' ? '' : text.endsWith('\n') ? text : `${text}\n`;
    return `${prefix}${renderPair(key, nested, 0, nestedOptions, indentSeq)}\n`;
  }
  if (!isMap(parent)) {
    return null;
  }
  if (parent.flow || parent.items.length === 0) {
    return rewriteFlowCollection(text, document, parentPath, (node) => {
      node.setIn([key], nested);
    });
  }
  const last = parent.items.at(-1) as Pair;
  const firstKey = rangeOf((parent.items[0] as Pair).key);
  if (options.first && firstKey) {
    const before = lineStart(text, firstKey[0]);
    // Only when the first key starts its line; `- a: 1` keeps new keys after it instead.
    if (text.slice(before, firstKey[0]).trim() === '') {
      return splice(
        text,
        before,
        before,
        `${renderPair(key, nested, columnOf(text, firstKey[0]), nestedOptions, indentSeq)}\n`,
      );
    }
  }
  const lastEnd = last.value && rangeOf(last.value) ? contentEnd(text, last.value) : (rangeOf(last.key)?.[1] ?? 0);
  if (!firstKey) {
    return null;
  }
  const at = lineEnd(text, lastEnd);
  const newline = at === text.length && !text.endsWith('\n') ? '\n' : '';
  return splice(
    text,
    at,
    at,
    `${newline}${renderPair(key, nested, columnOf(text, firstKey[0]), nestedOptions, indentSeq)}\n`,
  );
}

/** Writes one flow (or empty) collection anew, after `change`, keeping it on its line. */
function rewriteFlowCollection(
  text: string,
  document: Document,
  path: Path,
  change: (node: Collection) => void,
): string | null {
  const node = (path.length === 0 ? document.contents : document.getIn(path, true)) as Collection | null;
  const range = rangeOf(node);
  if (!node || !range || (!isMap(node) && !isSeq(node))) {
    return null;
  }
  const copy = node.clone() as Collection;
  // Comments around the collection stay in the text; written again they would appear twice.
  copy.comment = null;
  copy.commentBefore = null;
  change(copy);
  copy.flow = true;
  const replacement = new Document(copy).toString(renderOptions).replace(/\n$/, '');
  return splice(text, range[0], node.flow ? range[1] : contentEnd(text, node), replacement);
}

/** Removes the value at `path` and its key. */
export function deleteIn(text: string, path: Path): EditResult {
  const document = readable(text);
  const indentSeq = sequencesIndented(text);
  if (path.length === 0 || !document.hasIn([...path])) {
    return { text, fallback: false };
  }
  const expected = document.clone();
  expected.deleteIn([...path]);
  const key = path.at(-1);
  if (typeof key === 'number') {
    return removeItem(text, path.slice(0, -1), key);
  }
  const parentPath = path.slice(0, -1);
  const parent = parentPath.length === 0 ? document.contents : document.getIn(parentPath, true);
  if (!isMap(parent) || key === undefined) {
    return verified(null, expected, indentSeq);
  }
  if (parent.flow || parent.items.length === 1) {
    // A flow mapping is written anew; a block mapping losing its only pair becomes `{}`.
    const spliced = parent.flow
      ? rewriteFlowCollection(text, document, parentPath, (node) => node.deleteIn([key]))
      : rewriteEmptied(text, parent, '{}');
    return verified(spliced, expected, indentSeq);
  }
  const pair = pairOf(parent, key);
  const keyRange = rangeOf(pair?.key);
  if (!pair || !keyRange) {
    return verified(null, expected, indentSeq);
  }
  const end = pair.value && rangeOf(pair.value) ? contentEnd(text, pair.value) : keyRange[1];
  return verified(splice(text, lineStart(text, keyRange[0]), lineEnd(text, end), ''), expected, indentSeq);
}

/** Replaces a block collection's content with an empty flow one, keeping it under its key. */
function rewriteEmptied(text: string, node: Collection, empty: string): string | null {
  const range = rangeOf(node);
  if (!range) {
    return null;
  }
  // From the end of the key's line to the end of the content: `key:\n  a: 1` becomes `key: {}`.
  const keyLineEnd = text.lastIndexOf('\n', range[0] - 1);
  if (keyLineEnd === -1) {
    return splice(text, range[0], contentEnd(text, node), empty);
  }
  return splice(text, keyLineEnd, contentEnd(text, node), ` ${empty}`);
}

/** Adds an item at the end of the list at `path`, creating the list when there is none. */
export function appendItem(text: string, path: Path, value: unknown, options: ValueOptions = {}): EditResult {
  const document = readable(text);
  const indentSeq = sequencesIndented(text);
  const list = document.getIn([...path], true);
  if (!isSeq(list)) {
    return setIn(text, path, [value], { flow: options.flow ?? true, first: options.first ?? false });
  }
  const expected = document.clone();
  expected.addIn([...path], valueNode(value, options, expected));
  if (list.flow || list.items.length === 0) {
    return verified(
      rewriteFlowCollection(text, document, path, (node) => {
        if (isSeq(node)) {
          node.add(valueNode(value, options, document));
        }
      }),
      expected,
      indentSeq,
    );
  }
  const last = list.items.at(-1);
  const lastRange = rangeOf(last);
  if (!lastRange) {
    return verified(null, expected, indentSeq);
  }
  const dash = text.lastIndexOf('-', lastRange[0]);
  const dashColumn = columnOf(text, dash);
  const at = lineEnd(text, contentEnd(text, last));
  const newline = at === text.length && !text.endsWith('\n') ? '\n' : '';
  const item =
    !isCollectionValue(value) || options.flow
      ? renderInline(value)
      : renderBlock(value, dashColumn + 2, indentSeq).slice(dashColumn + 2);
  return verified(splice(text, at, at, `${newline}${' '.repeat(dashColumn)}- ${item}\n`), expected, indentSeq);
}

/** Removes the item at `index` from the list at `path`; a list left empty becomes `[]`. */
export function removeItem(text: string, path: Path, index: number): EditResult {
  const document = readable(text);
  const indentSeq = sequencesIndented(text);
  const list = document.getIn([...path], true);
  if (!isSeq(list) || index < 0 || index >= list.items.length) {
    return { text, fallback: false };
  }
  const expected = document.clone();
  expected.deleteIn([...path, index]);
  if (list.flow) {
    return verified(
      rewriteFlowCollection(text, document, path, (node) => node.deleteIn([index])),
      expected,
      indentSeq,
    );
  }
  if (list.items.length === 1) {
    return verified(rewriteEmptied(text, list, '[]'), expected, indentSeq);
  }
  const item = list.items[index];
  const range = rangeOf(item);
  if (!range) {
    return verified(null, expected, indentSeq);
  }
  const dash = text.lastIndexOf('-', range[0]);
  return verified(splice(text, lineStart(text, dash), lineEnd(text, contentEnd(text, item)), ''), expected, indentSeq);
}
