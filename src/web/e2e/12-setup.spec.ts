import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test } from '@playwright/test';
import { docsShot, snapshot } from './helpers';
import { dockerAvailable, startBundledRunner } from './runner';
import { startSetupServer, type SetupServer } from './setupServer';

/*
 * A fresh installation is set up in the browser (ADR-0020), on a server of its own: opened without
 * the token in the address, as on a server without a browser, the token is typed in. With Docker,
 * a runner waits for the token the setup hands over, as the compose package's does, and runs the
 * example. The pictures of the installation guide come from here (`npm run docs:screenshots`).
 */

let server: SetupServer | null = null;
let runner: { stop(): Promise<void> } | null = null;
const withRunner = !!process.env['CI'] || dockerAvailable();

test.beforeAll(async () => {
  server = await startSetupServer(5094);
  if (withRunner) {
    runner = startBundledRunner('http://127.0.0.1:5095', server.runnerTokenFile);
  }
});

test.afterAll(async () => {
  await runner?.stop();
  await server?.stop();
});

test('a fresh installation is set up with the token from the log, then its addresses are changed', async ({ page }) => {
  test.setTimeout(240_000);
  if (!server) {
    throw new Error('No server.');
  }
  const { url, token, dataDirectory, runnerTokenFile } = server;

  // Any page leads to the setup while there is no account.
  await page.goto(`${url}/`);
  await expect(page.getByRole('heading', { name: 'Set up AutoCanvas' })).toBeVisible();
  await expect(page).toHaveURL(/\/setup$/);
  const steps = page.getByRole('list', { name: 'Setup steps' });
  await expect(steps.getByRole('listitem').first()).toHaveAttribute('aria-current', 'step');
  await snapshot(page, 'setup-1-token');

  const field = page.getByLabel('Setup token');
  await field.fill('not-the-token');
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page.getByText("This is not this server's setup token.", { exact: false })).toBeVisible();
  await field.fill(token);
  await docsShot(page, 'install-1-token');
  await page.getByRole('button', { name: 'Continue' }).click();

  // The first admin.
  await page.getByLabel('User name').fill('felix');
  await page.getByLabel('Display name').fill('Felix');
  await page.getByLabel('Password', { exact: true }).fill('a password of my own');
  await page.getByLabel('Repeat the password').fill('a password of my own');
  await snapshot(page, 'setup-2-admin');
  await docsShot(page, 'install-2-admin');
  await page.getByRole('button', { name: 'Continue' }).click();

  // The addresses, filled in from the browser.
  await expect(page.getByLabel('Public address')).toHaveValue(url);
  await expect(page.getByLabel('Runner address')).toHaveValue('http://127.0.0.1:5081');
  await page.getByLabel('Runner address').fill('http://127.0.0.1:5095');
  await snapshot(page, 'setup-3-addresses');
  await page.getByRole('button', { name: 'Continue' }).click();

  // The options, then the setup itself.
  await expect(page.getByLabel('Register the bundled runner')).toBeChecked();
  await expect(page.getByLabel('Create an example automation')).toBeChecked();
  await snapshot(page, 'setup-4-options');
  await docsShot(page, 'install-3-options');
  await page.getByRole('button', { name: 'Set up AutoCanvas' }).click();

  // The generated master key, shown once.
  await expect(page.getByRole('heading', { name: 'Back up the master key' })).toBeVisible();
  const key = readFileSync(join(dataDirectory, 'keys', 'master.key'), 'utf8').trim();
  await expect(page.getByLabel('Master key', { exact: true })).toHaveText(key);
  await snapshot(page, 'setup-5-master-key');
  await docsShot(page, 'install-4-master-key');
  await expect(page.getByRole('button', { name: 'Continue' })).toBeDisabled();
  await page.getByLabel('I have stored the master key').check();
  await page.getByRole('button', { name: 'Continue' }).click();

  expect(readFileSync(runnerTokenFile, 'utf8')).toMatch(/^acrt_/);
  if (withRunner) {
    // The runner registers itself with the token and comes online.
    await expect(page.getByText('The runner bundled is online.')).toBeVisible({ timeout: 60_000 });
  } else {
    // No runner reads the token here, so the setup goes on without it.
    await expect(page.getByRole('heading', { name: 'Waiting for the bundled runner' })).toBeVisible();
    await snapshot(page, 'setup-6-waiting-for-runner');
    await page.getByRole('button', { name: 'Continue without waiting' }).click();
  }
  await expect(page.getByRole('heading', { name: 'AutoCanvas is set up' })).toBeVisible();
  await snapshot(page, 'setup-7-done');
  await docsShot(page, 'install-5-done');
  await page.getByRole('link', { name: 'Open the example' }).click();
  await expect(page.getByRole('heading', { name: 'hello', level: 1 })).toBeVisible();

  if (withRunner) {
    // The first run: the example on the bundled runner, with its log, tests and coverage.
    await page.goto(`${page.url()}/runs`);
    await page.getByRole('button', { name: 'Run', exact: true }).first().click();
    await expect(page.getByRole('heading', { name: 'Run #1' })).toBeVisible();
    await expect(page.getByText('Succeeded', { exact: true }).first()).toBeVisible({ timeout: 120_000 });
    await expect(page.getByRole('log', { name: 'Lines of hello, attempt 1' })).toContainText('Hello from AutoCanvas');
    const views = page.getByRole('navigation', { name: 'Views of the run' });
    await expect(views.getByRole('link', { name: /Coverage 80 %/ })).toBeVisible();
    await docsShot(page, 'install-6-first-run');
  }

  // The addresses, later: Administration → Instance.
  await page.goto(`${url}/admin/instance`);
  await expect(page.getByLabel('Public address')).toHaveValue(url);
  await page.getByLabel('Public address').fill('http://autocanvas.example.org');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('Saved.')).toBeVisible();
  await snapshot(page, 'admin-3-instance');

  // Set up for good: the setup page leads into the app.
  await page.goto(`${url}/setup`);
  await expect(page.getByRole('heading', { name: 'Automations', level: 1 })).toBeVisible();
});
