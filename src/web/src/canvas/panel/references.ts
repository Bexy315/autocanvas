/** The names an automation's expressions may read, for inserting them into values. */
export interface References {
  variables: readonly string[];
  secrets: readonly string[];
}

export const noReferences: References = { variables: [], secrets: [] };

const reference = /^\$\{\{\s*(secrets|vars)\.([A-Za-z_][A-Za-z0-9_]*)\s*\}\}$/;

/** The variable or secret a value names as a whole, or null for anything else. */
export function referenceOf(value: string): { kind: 'secret' | 'variable'; name: string } | null {
  const found = reference.exec(value.trim());
  if (!found?.[2]) {
    return null;
  }
  return { kind: found[1] === 'secrets' ? 'secret' : 'variable', name: found[2] };
}
