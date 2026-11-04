// Takes the pictures of the user guide again: runs the smoke test (it sets the admin's password),
// the walk-through against the real server with a runner, the setup, the forges against a GitHub
// stand-in and the repositories against a local Git server (needs `npm run build`, the server and
// runner builds and Docker), then copies test-results/docs/*.png to docs/user/img/. Run it after a
// change that alters what the pictures show, and commit the result.
import { execFileSync } from 'node:child_process';
import { copyFileSync, mkdirSync, readdirSync, rmSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('..', import.meta.url));
const shots = fileURLToPath(new URL('../test-results/docs/', import.meta.url));
const target = fileURLToPath(new URL('../../../docs/user/img/', import.meta.url));
const cli = fileURLToPath(new URL('../node_modules/@playwright/test/cli.js', import.meta.url));

rmSync(shots, { recursive: true, force: true });
execFileSync(
  process.execPath,
  [cli, 'test', '01-smoke', '11-walk-through', '12-setup', '13-forges', '14-repositories'],
  {
    cwd: root,
    stdio: 'inherit',
  },
);

mkdirSync(target, { recursive: true });
const pictures = readdirSync(shots).filter((name) => name.endsWith('.png'));
for (const name of pictures) {
  copyFileSync(shots + name, target + name);
}
console.log(`Copied ${pictures.length} pictures to docs/user/img/.`);
