import { expect, test } from '@playwright/test';
import { GitServer } from './gitServer';
import { docsShot, signIn, snapshot } from './helpers';

const csrf = { 'X-AutoCanvas-Request': '1' };
const build = `version: 1
name: Build the shop
triggers:
  manual: true
  push:
    branches: [main]
jobs:
  build:
    runs_on: linux
    steps:
      - run: make
`;

test('a Git repository is connected in a folder, followed, given a webhook, run from, and disconnected', async ({
  page,
}) => {
  const git = new GitServer(5097);
  await git.start();
  const suffix = Math.random().toString(36).slice(2, 7);
  const name = `shop${suffix}`;
  git.create(`team/${name}`);
  const first = git.commit(`team/${name}`, { '.autocanvas/automations/build.yml': build }, 'Add the build');

  try {
    await signIn(page);
    const folder = await page.request.post('/api/v1/folders', { data: { name: `Repos ${suffix}` }, headers: csrf });
    const folderId = ((await folder.json()) as { id: string }).id;

    // Connect it by its address.
    await page.goto(`/folders/${folderId}/repositories`);
    await expect(page.getByRole('heading', { name: 'No repositories' })).toBeVisible();
    await page.getByRole('button', { name: 'Connect a repository' }).click();
    const connect = page.getByRole('dialog', { name: 'Connect a repository' });
    await connect.getByRole('button', { name: 'Git address' }).click();
    await connect.getByLabel('Address').fill(git.url(`team/${name}`));
    await connect.getByLabel('Poll every (minutes)').fill('0');
    await docsShot(page, 'repository-connect');
    await connect.getByRole('button', { name: 'Connect', exact: true }).click();
    const card = page.getByRole('list', { name: 'Repositories' }).getByRole('listitem').first();
    await expect(card.getByRole('heading', { name })).toBeVisible();
    await expect(card.getByText('1 automation')).toBeVisible();
    await expect(card.getByText(first.slice(0, 7))).toBeVisible();

    // A new file in the repository: Sync reads it.
    git.commit(
      `team/${name}`,
      { '.autocanvas/automations/deploy.yml': build.replace('Build the shop', 'Deploy') },
      'Add deploy',
    );
    await card.getByRole('button', { name: 'Sync' }).click();
    await expect(card.getByText('2 automations')).toBeVisible();
    await docsShot(page, 'repositories');
    await snapshot(page, '14-repositories');

    // A webhook address, shown once.
    await page.getByRole('button', { name: `Actions for ${name}` }).click();
    await page.getByRole('menuitem', { name: 'Create webhook address' }).click();
    const webhook = page.getByRole('dialog', { name: 'Webhook address' });
    await expect(webhook.getByText(/\/api\/v1\/hooks\/acg_/)).toBeVisible();
    await snapshot(page, '14-repositories-webhook');
    await webhook.getByLabel('I have entered them').check();
    await webhook.getByRole('button', { name: 'Done' }).click();
    await expect(card.getByText(/webhook acg_/)).toBeVisible();

    // Its automations: read-only here, from the repository, and runs know their commit.
    await page.goto(`/folders/${folderId}`);
    await expect(page.getByText(`From ${name}`).first()).toBeVisible();
    await page.getByRole('link', { name: /Build the shop/ }).click();
    const from = page.getByRole('region', { name: 'From repository' });
    await expect(from.getByText('.autocanvas/automations/build.yml')).toBeVisible();
    await docsShot(page, 'repository-automation');
    await page.getByRole('button', { name: 'Automation actions' }).click();
    await expect(page.getByRole('menuitem')).toHaveText(['Download']);
    await page.keyboard.press('Escape');
    await page.getByRole('button', { name: 'Run', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Run #1' })).toBeVisible();
    await expect(page.getByText(/ on main/)).toBeVisible();
    await snapshot(page, '14-repositories-run');
    const run = /\/runs\/([^/?]+)/.exec(page.url())?.[1] ?? '';
    await page.request.post(`/api/v1/runs/${run}/cancel`, { headers: csrf });

    // Disconnected: its automations go, nothing changes in the repository.
    await page.goto(`/folders/${folderId}/repositories`);
    await page.getByRole('button', { name: `Actions for ${name}` }).click();
    await page.getByRole('menuitem', { name: 'Disconnect' }).click();
    await page
      .getByRole('dialog', { name: `Disconnect ${name}?` })
      .getByRole('button', { name: 'Disconnect' })
      .click();
    await expect(page.getByRole('heading', { name: 'No repositories' })).toBeVisible();
  } finally {
    await git.stop();
  }
});
