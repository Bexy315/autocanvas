// Publishes the JSON Schemas from ../schema under /schema/ on the docs site, so their $id URLs
// resolve and editors can fetch them. Runs before `start` and `build`.
import { cpSync, mkdirSync, readdirSync, rmSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = dirname(dirname(fileURLToPath(import.meta.url)));
const source = join(root, '..', 'schema');
const target = join(root, 'static', 'schema');

rmSync(target, { recursive: true, force: true });
mkdirSync(target, { recursive: true });
for (const file of readdirSync(source).filter((name) => name.endsWith('.json'))) {
  cpSync(join(source, file), join(target, file));
}
