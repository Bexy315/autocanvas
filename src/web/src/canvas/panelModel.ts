/**
 * What the side panel reads from the file. Values come from the YAML document as plain data; the
 * panel shows each in a form only when the value has the shape that form writes back (a mapping of
 * plain values for `env`, a list of texts for `runs_on`, …). Anything else, such as an expression in
 * place of a mapping, or an anchor shared with another place, is shown as it is written, to be
 * changed in the YAML: the panel never overwrites what it cannot show.
 */
import { isAlias, isCollection, parseDocument, visit, type Document } from 'yaml';
import type { Diagnostic } from '../api/problem';
import type { Path } from './yamlEdit';

export type Mapping = Record<string, unknown>;

export interface PanelFile {
  /** The value at a path as plain data; undefined when there is none. */
  get(path: Path): unknown;
  /** Whether the value at a path uses an alias or an anchor, so changing it here would change more. */
  shared(path: Path): boolean;
  /** The value at a path as it is written in the file, for showing what the panel cannot. */
  source(path: Path): string;
}

/** The file for the panel, or null while it cannot be read. */
export function readPanelFile(text: string): PanelFile | null {
  const document = parseDocument(text);
  if (document.errors.length > 0) {
    return null;
  }
  return {
    get: (path) => toData(document, path),
    shared: (path) => isShared(document, path),
    source: (path) => sourceOf(text, document, path),
  };
}

function toData(document: Document, path: Path): unknown {
  const node: unknown = path.length === 0 ? document.contents : document.getIn([...path], true);
  if (node === undefined || node === null) {
    return undefined;
  }
  const json = (node as { toJSON?: () => unknown }).toJSON;
  return typeof json === 'function' ? json.call(node) : node;
}

function isShared(document: Document, path: Path): boolean {
  const node: unknown = path.length === 0 ? document.contents : document.getIn([...path], true);
  if (isAlias(node) || (node as { anchor?: string } | null)?.anchor) {
    return true;
  }
  if (!isCollection(node)) {
    return false;
  }
  let found = false;
  visit(node, {
    Node: (_key, inner) => {
      if (isAlias(inner) || inner.anchor) {
        found = true;
        return visit.BREAK;
      }
      return undefined;
    },
  });
  return found;
}

function sourceOf(text: string, document: Document, path: Path): string {
  const node = (path.length === 0 ? document.contents : document.getIn([...path], true)) as {
    range?: [number, number, number];
  } | null;
  const range = node?.range;
  return range ? text.slice(range[0], range[1]).trimEnd() : '';
}

/** A mapping of plain values, as `env` and `variables` are written. */
export function isPlainMap(value: unknown): value is Readonly<Record<string, string | number | boolean>> {
  return (
    typeof value === 'object' &&
    value !== null &&
    !Array.isArray(value) &&
    Object.values(value).every((inner) => ['string', 'number', 'boolean'].includes(typeof inner))
  );
}

export function isMapping(value: unknown): value is Mapping {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/** A list of texts, as `runs_on` and branch patterns are written. */
export function isTextList(value: unknown): value is readonly string[] {
  return Array.isArray(value) && value.every((inner) => typeof inner === 'string');
}

/** A list of mappings, as steps, reports and schedules are written. */
export function isMappingList(value: unknown): value is readonly Mapping[] {
  return Array.isArray(value) && value.every(isMapping);
}

/**
 * The React key of a row that stands for a place in a list, such as `steps[2]`. The row's content
 * changes with every key typed into it, so the place is its identity, not the content.
 */
export function positionKey(index: number): string {
  return `at-${index}`;
}

/** A path as the server writes it in diagnostics: `jobs.build.steps[0].run`. */
export function pathText(path: Path): string {
  return path.reduce<string>(
    (text, segment) =>
      typeof segment === 'number' ? `${text}[${segment}]` : text === '' ? segment : `${text}.${segment}`,
    '',
  );
}

/** The messages of the diagnostics about exactly this path. */
export function problemsAt(diagnostics: readonly Diagnostic[], path: Path): string[] {
  const at = pathText(path);
  return diagnostics.filter((diagnostic) => diagnostic.path === at).map((diagnostic) => diagnostic.message);
}

/** How many diagnostics are about this path or anything inside it. */
export function problemsUnder(diagnostics: readonly Diagnostic[], path: Path): number {
  const at = pathText(path);
  return diagnostics.filter(
    (diagnostic) =>
      diagnostic.path === at || diagnostic.path.startsWith(`${at}.`) || diagnostic.path.startsWith(`${at}[`),
  ).length;
}
