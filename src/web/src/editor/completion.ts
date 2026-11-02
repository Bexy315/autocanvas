import type { Completion, CompletionContext, CompletionResult } from '@codemirror/autocomplete';
import type { components } from '../api/schema';
import { keysAt, valuesAt, type Schema } from './schemaKeys';
import { item, placeAt, type CursorPlace } from './yamlPath';

export type NodeType = components['schemas']['NodeTypeView'];

const kindHints: Record<string, string> = {
  Text: 'text',
  WholeNumber: 'whole number',
  Boolean: 'true or false',
  TextList: 'list of texts',
  TextMap: 'mapping of names to texts',
};

/** Whether the place is inside a step: a list item directly under `steps`. */
function inStep(path: readonly string[]): boolean {
  return path.at(-2) === 'steps' && path.at(-1) === item;
}

/** What may be typed at `place`: keys of the mapping, or values of the key. */
export function optionsFor(place: CursorPlace, schema: Schema, nodeTypes: readonly NodeType[]): Completion[] {
  if (place.mode === 'key') {
    // Keys under a step's `with` come from the node type the step uses.
    if (place.path.at(-1) === 'with' && inStep(place.path.slice(0, -1))) {
      const type = nodeTypes.find((node) => node.name === place.itemValues['type']);
      return (type?.inputs ?? [])
        .filter((input) => !place.siblings.includes(input.name))
        .map((input, index) => ({
          label: input.name,
          type: 'property',
          detail: `${input.required ? 'required, ' : ''}${kindHints[input.kind] ?? input.kind}`,
          info: input.description,
          apply: `${input.name}: `,
          boost: input.required ? 10 - index / 100 : -index / 100,
        }));
    }
    return keysAt(schema, place.path)
      .filter((key) => !place.siblings.includes(key.name))
      .map((key) => {
        const option: Completion = { label: key.name, type: 'property', apply: `${key.name}: ` };
        if (key.description) {
          option.info = key.description;
        }
        return option;
      });
  }

  const key = place.key ?? '';
  if (key === 'type' && inStep(place.path)) {
    return nodeTypes.map((node) => ({ label: node.name, type: 'enum', info: node.description }));
  }
  // A choice of a node input, such as http_request's method.
  if (place.path.at(-1) === 'with' && inStep(place.path.slice(0, -1))) {
    const type = nodeTypes.find((node) => node.name === place.itemValues['type']);
    const input = type?.inputs.find((candidate) => candidate.name === key);
    return (input?.choices ?? []).map((choice) => ({ label: choice, type: 'enum' }));
  }
  return valuesAt(schema, [...place.path, key]).map((value) => ({ label: value, type: 'enum' }));
}

/** A CodeMirror completion source for automation files. */
export function automationCompletions(schema: Schema, nodeTypes: () => readonly NodeType[]) {
  return (context: CompletionContext): CompletionResult | null => {
    const text = context.state.doc.toString();
    const place = placeAt(text, context.pos);
    if (!place || (place.prefix === '' && !context.explicit && place.mode === 'key')) {
      return null;
    }
    const options = optionsFor(place, schema, nodeTypes());
    if (options.length === 0) {
      return null;
    }
    return { from: place.from, options, validFor: /^[A-Za-z0-9_-]*$/ };
  };
}
