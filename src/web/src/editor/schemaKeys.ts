import { item } from './yamlPath';

/** A JSON Schema as far as completions read it. */
export interface Schema {
  $ref?: string;
  description?: string;
  type?: string | string[];
  properties?: Record<string, Schema>;
  patternProperties?: Record<string, Schema>;
  additionalProperties?: Schema | boolean;
  items?: Schema;
  allOf?: Schema[];
  anyOf?: Schema[];
  oneOf?: Schema[];
  if?: Schema;
  then?: Schema;
  else?: Schema;
  enum?: unknown[];
  const?: unknown;
  default?: unknown;
  $defs?: Record<string, Schema>;
}

export interface KeyOption {
  name: string;
  description: string;
}

/** The schemas a schema stands for: itself, what it refers to, and every branch it combines. */
function expand(schema: Schema | undefined, root: Schema, seen = new Set<Schema>()): Schema[] {
  if (!schema || seen.has(schema)) {
    return [];
  }
  seen.add(schema);
  const result: Schema[] = [schema];
  if (schema.$ref?.startsWith('#/$defs/')) {
    result.push(...expand(root.$defs?.[schema.$ref.slice('#/$defs/'.length)], root, seen));
  }
  for (const branch of [...(schema.allOf ?? []), ...(schema.anyOf ?? []), ...(schema.oneOf ?? [])]) {
    result.push(...expand(branch, root, seen));
  }
  for (const branch of [schema.then, schema.else]) {
    result.push(...expand(branch, root, seen));
  }
  return result;
}

/** The schemas of what is at `path` below `root`. */
export function schemasAt(root: Schema, path: readonly string[]): Schema[] {
  let current = expand(root, root);
  for (const segment of path) {
    const next: Schema[] = [];
    for (const schema of current) {
      if (segment === item) {
        next.push(...expand(schema.items, root));
        continue;
      }
      const property = schema.properties?.[segment];
      if (property) {
        next.push(...expand(property, root));
        continue;
      }
      for (const [pattern, child] of Object.entries(schema.patternProperties ?? {})) {
        if (new RegExp(pattern, 'u').test(segment)) {
          next.push(...expand(child, root));
        }
      }
      if (typeof schema.additionalProperties === 'object') {
        next.push(...expand(schema.additionalProperties, root));
      }
    }
    current = next;
  }
  return current;
}

/** The keys a mapping at `path` may have, with what each means; extension keys (`x-…`) are left out. */
export function keysAt(root: Schema, path: readonly string[]): KeyOption[] {
  const keys = new Map<string, string>();
  for (const schema of schemasAt(root, path)) {
    for (const [name, property] of Object.entries(schema.properties ?? {})) {
      const description = property.description ?? expand(property, root).find((s) => s.description)?.description ?? '';
      if (!keys.has(name) || (keys.get(name) === '' && description !== '')) {
        keys.set(name, description);
      }
    }
  }
  return [...keys].map(([name, description]) => ({ name, description }));
}

/** The values a key at `path` takes, when the schema names them: enums, constants, booleans. */
export function valuesAt(root: Schema, path: readonly string[]): string[] {
  const values = new Set<string>();
  for (const schema of schemasAt(root, path)) {
    for (const value of schema.enum ?? []) {
      values.add(String(value));
    }
    if (schema.const !== undefined) {
      values.add(String(schema.const));
    }
    const types = Array.isArray(schema.type) ? schema.type : schema.type ? [schema.type] : [];
    if (types.includes('boolean')) {
      values.add('true');
      values.add('false');
    }
  }
  return [...values];
}
