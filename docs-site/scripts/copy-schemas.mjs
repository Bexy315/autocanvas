// Publishes the JSON Schemas from ../schema under /schema/ on the docs site, so their $id URLs
// resolve and editors can fetch them, and the OpenAPI document from ../openapi under /openapi/ for
// the API reference page, together with the pinned Scalar viewer that renders it, and a few pictures
// of the UI from ../docs/user/img under /img/ui/ for the home page. Runs before `start` and `build`.
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

const openapiSource = join(root, '..', 'openapi');
const openapiTarget = join(root, 'static', 'openapi');
rmSync(openapiTarget, { recursive: true, force: true });
mkdirSync(openapiTarget, { recursive: true });
for (const file of readdirSync(openapiSource).filter((name) => name.endsWith('.json'))) {
  cpSync(join(openapiSource, file), join(openapiTarget, file));
}

const scalarTarget = join(root, 'static', 'scalar');
rmSync(scalarTarget, { recursive: true, force: true });
mkdirSync(scalarTarget, { recursive: true });
cpSync(join(root, 'node_modules', '@scalar', 'api-reference', 'dist', 'browser', 'standalone.js'), join(scalarTarget, 'standalone.js'));

// The home page shows these pictures of the UI; the docs pages use the same files directly.
const uiSource = join(root, '..', 'docs', 'user', 'img');
const uiTarget = join(root, 'static', 'img', 'ui');
rmSync(uiTarget, { recursive: true, force: true });
mkdirSync(uiTarget, { recursive: true });
for (const name of ['tour-editor', 'tour-run-log', 'tour-tests', 'repositories']) {
  for (const scheme of ['light', 'dark']) {
    cpSync(join(uiSource, `${name}-${scheme}.png`), join(uiTarget, `${name}-${scheme}.png`));
  }
}
