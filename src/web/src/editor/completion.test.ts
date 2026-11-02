import { Text } from '@codemirror/state';
import { describe, expect, it } from 'vitest';
import { automationSchema } from './automationSchema';
import { optionsFor, type NodeType } from './completion';
import { offsetOf, toEditorDiagnostics } from './diagnostics';
import { keysAt, valuesAt } from './schemaKeys';
import { item, placeAt } from './yamlPath';

/** The place at the `|` in `text`. */
function at(text: string) {
  const offset = text.indexOf('|');
  return placeAt(text.replace('|', ''), offset);
}

const nodeTypes: NodeType[] = [
  {
    name: 'http_request',
    description: 'Sends an HTTP request.',
    inputs: [
      { name: 'url', kind: 'Text', required: true, description: 'The address.', choices: [] },
      { name: 'method', kind: 'Text', required: false, description: 'The method.', choices: ['GET', 'POST'] },
      { name: 'timeout_seconds', kind: 'WholeNumber', required: false, description: 'How long.', choices: [] },
    ],
    outputs: [{ name: 'status', description: 'The status.' }],
  },
  { name: 'docker_build', description: 'Builds an image.', inputs: [], outputs: [] },
];

describe('where the cursor is', () => {
  it('knows a key typed at the top', () => {
    expect(at('version: 1\ntri|')).toMatchObject({ path: [], mode: 'key', prefix: 'tri', siblings: ['version'] });
  });

  it('follows indentation into jobs and steps', () => {
    const place = at('jobs:\n  build:\n    runs_on: linux\n    steps:\n      - run: make\n        ti|');
    expect(place).toMatchObject({
      path: ['jobs', 'build', 'steps', item],
      mode: 'key',
      prefix: 'ti',
      siblings: ['run'],
    });
  });

  it('knows a new list item', () => {
    expect(at('jobs:\n  build:\n    steps:\n      - |')).toMatchObject({
      path: ['jobs', 'build', 'steps', item],
      prefix: '',
    });
  });

  it('knows the value of a key, and the keys of the step around it', () => {
    const place = at(
      'jobs:\n  b:\n    steps:\n      - type: http_request\n        with:\n          method: P|\n          url: x',
    );
    expect(place).toMatchObject({
      path: ['jobs', 'b', 'steps', item, 'with'],
      mode: 'value',
      key: 'method',
      prefix: 'P',
      siblings: ['url'],
    });
    expect(place?.itemValues['type']).toBe('http_request');
  });

  it('skips blank lines and comments above', () => {
    expect(at('jobs:\n\n  # a job\n  build:\n    im|')?.path).toEqual(['jobs', 'build']);
  });

  it('offers nothing inside a comment', () => {
    expect(at('# tri|')).toBeNull();
    expect(at('name: "a b|')).toBeNull();
  });

  it('starts the completion where the typed word starts', () => {
    const text = 'version: 1\nconc|';
    expect(at(text)?.from).toBe('version: 1\n'.length);
  });
});

describe('what the schema allows', () => {
  it('lists the top-level keys with their meaning', () => {
    const keys = keysAt(automationSchema, []);
    expect(keys.map((key) => key.name)).toEqual(expect.arrayContaining(['version', 'name', 'triggers', 'jobs']));
    expect(keys.find((key) => key.name === 'concurrency')?.description).toContain('overlap');
  });

  it('merges the kinds of jobs and steps', () => {
    const jobKeys = keysAt(automationSchema, ['jobs', 'build']).map((key) => key.name);
    expect(jobKeys).toEqual(expect.arrayContaining(['runs_on', 'steps', 'needs', 'expression', 'cases']));
    const stepKeys = keysAt(automationSchema, ['jobs', 'build', 'steps', item]).map((key) => key.name);
    expect(stepKeys).toEqual(expect.arrayContaining(['run', 'type', 'with', 'id', 'if']));
  });

  it('knows the values of enums and booleans', () => {
    expect(valuesAt(automationSchema, ['concurrency'])).toEqual(expect.arrayContaining(['single', 'parallel']));
    expect(valuesAt(automationSchema, ['triggers', 'manual'])).toEqual(['true', 'false']);
    expect(valuesAt(automationSchema, ['nothing', 'here'])).toEqual([]);
  });
});

/** The labels offered at the `|` in `text`. */
const labels = (text: string) => {
  const place = at(text);
  return place ? optionsFor(place, automationSchema, nodeTypes).map((option) => option.label) : [];
};

describe('completion options', () => {
  it('offer the keys not given yet', () => {
    const options = labels('version: 1\n|');
    expect(options).toContain('jobs');
    expect(options).not.toContain('version');
  });

  it('offer the node types as the value of a step type', () => {
    expect(labels('jobs:\n  b:\n    steps:\n      - type: h|')).toEqual(['http_request', 'docker_build']);
  });

  it("offer a node type's inputs under with, required ones first", () => {
    const place = at('jobs:\n  b:\n    steps:\n      - type: http_request\n        with:\n          |');
    const options = place ? optionsFor(place, automationSchema, nodeTypes) : [];
    expect(options.map((option) => option.label)).toEqual(['url', 'method', 'timeout_seconds']);
    expect(options[0]).toMatchObject({ detail: 'required, text', apply: 'url: ' });
    expect(options[2]?.detail).toBe('whole number');
  });

  it("offer an input's choices as its values", () => {
    expect(labels('jobs:\n  b:\n    steps:\n      - type: http_request\n        with:\n          method: |')).toEqual([
      'GET',
      'POST',
    ]);
  });

  it('offer nothing under with for an unknown node type', () => {
    expect(labels('jobs:\n  b:\n    steps:\n      - type: send_mail\n        with:\n          |')).toEqual([]);
  });

  it('offer schema values elsewhere', () => {
    expect(labels('concurrency: |')).toEqual(expect.arrayContaining(['single', 'parallel']));
  });
});

describe('diagnostics in the editor', () => {
  const doc = Text.of(['version: 1', 'jobs:', '  build:', '    needs: [tset]']);

  it('point at the line and column the server named', () => {
    expect(offsetOf(doc, 4, 13)).toBe(doc.line(4).from + 12);
    expect(offsetOf(doc, '2', null)).toBe(doc.line(2).from);
  });

  it('stay inside the file when the place is off', () => {
    expect(offsetOf(doc, null, null)).toBe(0);
    expect(offsetOf(doc, 99, 1)).toBe(doc.line(4).from);
    expect(offsetOf(doc, 1, 500)).toBe(doc.line(1).to);
  });

  it('mark the word at the place, with the code as source', () => {
    const [marker] = toEditorDiagnostics(doc, [
      {
        severity: 'Error',
        code: 'AC0201',
        message: "There is no job 'tset'.",
        path: 'jobs.build.needs[0]',
        line: 4,
        column: 13,
      },
    ]);
    expect(doc.sliceString(marker?.from ?? 0, marker?.to ?? 0)).toBe('tset');
    expect(marker).toMatchObject({ severity: 'error', source: 'AC0201' });
  });

  it('mark at least one character, and warnings as warnings', () => {
    const [marker] = toEditorDiagnostics(doc, [
      { severity: 'Warning', code: 'AC0900', message: 'Look here.', path: '', line: 2, column: 6 },
    ]);
    expect(marker).toMatchObject({ severity: 'warning' });
    expect((marker?.to ?? 0) - (marker?.from ?? 0)).toBeLessThanOrEqual(1);
  });
});
