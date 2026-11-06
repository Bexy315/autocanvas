import { expect, test } from '@playwright/test';
import { signIn, snapshot } from './helpers';

const broken = `version: 1
name: Broken
triggers:
  manual: true
jobs:
  build:
    needs: [tset]
    steps:
      - run: make
`;

test('folders and automations are created, compared, renamed, moved and deleted', async ({ page }) => {
  await signIn(page);

  // A folder at the top level, and one inside it.
  await page.getByRole('button', { name: 'New folder' }).first().click();
  await page.getByRole('textbox', { name: 'Name' }).fill('Team');
  await page.getByLabel('Description').fill('Everything the team builds');
  await page.getByRole('button', { name: 'Create folder' }).click();
  await expect(page.getByRole('heading', { name: 'Team', level: 1 })).toBeVisible();
  await page.getByRole('button', { name: 'New folder' }).click();
  await page.getByRole('textbox', { name: 'Name' }).fill('Backend');
  await page.getByRole('button', { name: 'Create folder' }).click();
  await expect(page.getByRole('heading', { name: 'Backend', level: 1 })).toBeVisible();
  const tree = page.getByRole('tree', { name: 'Folders and automations' });
  await expect(tree.getByRole('treeitem', { name: 'Backend' })).toHaveAttribute('aria-selected', 'true');
  await snapshot(page, 'folders-1-empty-folder');

  // An automation from the starter file: the server accepts it as it is.
  await page.getByRole('button', { name: 'New automation' }).first().click();
  await page.getByRole('textbox', { name: 'Name' }).fill('build');
  await page.getByLabel('Message').fill('First version');
  await snapshot(page, 'folders-2-new-automation');
  await page.getByRole('button', { name: 'Create automation' }).click();
  await expect(page.getByRole('heading', { name: 'build', level: 1 })).toBeVisible();
  await expect(page.getByText('By hand', { exact: true })).toBeVisible();
  const id = new URL(page.url()).pathname.split('/').at(-1) ?? '';
  // The tree shows it in its folder, chosen.
  await expect(tree.getByRole('treeitem', { name: 'build' })).toHaveAttribute('aria-selected', 'true');

  // A file with a mistake is refused with the problem, its place and its code.
  await page.getByRole('link', { name: 'Backend' }).click();
  await page.getByRole('button', { name: 'New automation' }).first().click();
  await page.getByLabel(/A file from your computer/).check();
  await page
    .getByLabel('Automation file', { exact: true })
    .setInputFiles({ name: 'broken.yaml', mimeType: 'application/yaml', buffer: Buffer.from(broken) });
  await page.getByRole('button', { name: 'Create automation' }).click();
  await expect(page.getByRole('list', { name: 'Problems in the file' })).toContainText('tset');
  await snapshot(page, 'folders-3-problems');
  await page.getByRole('button', { name: 'Cancel' }).click();
  await snapshot(page, 'folders-4-folder');

  // A second version, saved through the API as a script would, then compared in the UI.
  const current = await page.request.get(`/api/v1/automations/${id}`);
  const yaml = ((await current.json()) as { yaml: string }).yaml.replace('Hello from AutoCanvas', 'Hello again');
  const saved = await page.request.put(`/api/v1/automations/${id}`, {
    data: { yaml, message: 'Say hello again' },
    headers: { 'X-AutoCanvas-Request': '1', 'If-Match': current.headers()['etag'] ?? '*' },
  });
  expect(saved.ok()).toBeTruthy();
  await page.goto(`/automations/${id}`);
  await expect(page.getByText('Version 2', { exact: true })).toBeVisible();
  await snapshot(page, 'folders-5-overview');
  await page.getByRole('link', { name: 'Versions' }).click();
  const changes = page.getByRole('region', { name: 'Changes from version 1' });
  await expect(changes).toContainText('Hello again');
  await snapshot(page, 'folders-6-versions');

  // Rename, move to Team, and delete.
  await page.getByRole('button', { name: 'Automation actions' }).click();
  await page.getByRole('menuitem', { name: 'Rename' }).click();
  await page.getByRole('textbox', { name: 'Name' }).fill('build-shop');
  await page.getByRole('button', { name: 'Rename' }).click();
  await expect(page.getByRole('dialog')).toBeHidden();
  await expect(page.getByText('build-shop', { exact: true }).first()).toBeVisible();

  await page.getByRole('button', { name: 'Automation actions' }).click();
  await page.getByRole('menuitem', { name: 'Move' }).click();
  const picker = page.getByRole('tree', { name: 'Target folder' });
  await picker.getByRole('button', { name: 'Team' }).click();
  await snapshot(page, 'folders-7-move');
  await page.getByRole('button', { name: 'Move here' }).click();
  await expect(page.getByRole('dialog')).toBeHidden();
  await expect(page.getByRole('navigation', { name: 'Breadcrumbs' })).toContainText('Team');
  await expect(page.getByRole('navigation', { name: 'Breadcrumbs' })).not.toContainText('Backend');

  await page.getByRole('button', { name: 'Automation actions' }).click();
  await page.getByRole('menuitem', { name: 'Delete' }).click();
  await page.getByRole('button', { name: 'Delete automation' }).click();
  await expect(page.getByRole('heading', { name: 'Team', level: 1 })).toBeVisible();
  await expect(page.getByRole('link', { name: /build-shop/ })).toHaveCount(0);
  await snapshot(page, 'folders-8-team');
});
