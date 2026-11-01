// Fails when an npm package of the web UI (direct or transitive, runtime or build) has a license
// outside build/allowed-licenses.json, the list NuGet packages are checked against as well.
// license-exceptions.json names the few packages allowed with another license, each with a reason
// (ADR-0019); an exception covers the package and its platform builds (`name-*`).
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const readJson = (relative) => JSON.parse(readFileSync(new URL(relative, import.meta.url), 'utf8'));
const allowed = readJson('../../../build/allowed-licenses.json');
const exceptions = readJson('../license-exceptions.json');

const root = fileURLToPath(new URL('..', import.meta.url));
const bin = fileURLToPath(
  new URL('../node_modules/license-checker-rseidelsohn/bin/license-checker-rseidelsohn.js', import.meta.url),
);
const output = execFileSync(process.execPath, [bin, '--json', '--excludePrivatePackages', '--start', root], {
  encoding: 'utf8',
  maxBuffer: 64 * 1024 * 1024,
});
const packages = JSON.parse(output);

// An SPDX expression is fine when one side of every OR, or every part of an AND, is allowed.
function isAllowed(expression) {
  const text = String(expression)
    .replace(/^\((.*)\)$/, '$1')
    .trim();
  if (text.includes(' OR ')) {
    return text.split(' OR ').some((part) => isAllowed(part));
  }
  if (text.includes(' AND ')) {
    return text.split(' AND ').every((part) => isAllowed(part));
  }
  return allowed.includes(text);
}

function isExcepted(name, license) {
  return exceptions.some(
    (exception) =>
      (name === exception.package || name.startsWith(`${exception.package}-`)) && license === exception.license,
  );
}

const refused = Object.entries(packages)
  .map(([id, info]) => {
    const name = id.slice(0, id.lastIndexOf('@'));
    const license = Array.isArray(info.licenses) ? info.licenses.join(' AND ') : (info.licenses ?? 'UNKNOWN');
    return { id, name, license };
  })
  .filter(({ name, license }) => !isAllowed(license) && !isExcepted(name, license));

if (refused.length > 0) {
  console.error('Packages with a license outside build/allowed-licenses.json and license-exceptions.json:');
  for (const { id, license } of refused) {
    console.error(`  ${id}: ${license}`);
  }
  process.exit(1);
}

console.log(`${Object.keys(packages).length} packages, all with allowed licenses.`);
