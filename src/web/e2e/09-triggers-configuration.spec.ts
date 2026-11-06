import { createHmac } from 'node:crypto';
import { expect, test } from '@playwright/test';
import { signIn, snapshot } from './helpers';

const file = `version: 1
name: Deploy
triggers:
  manual: true
  webhook: true
  schedule:
    - cron: "0 6 * * 1-5"
      timezone: Europe/Berlin
variables:
  REGION: eu-west
jobs:
  deploy:
    runs_on: linux
    steps:
      - run: ./deploy \${{ vars.REGION }} \${{ vars.CHANNEL }} \${{ secrets.TOKEN }}
`;

const csrf = { 'X-AutoCanvas-Request': '1' };

test('triggers show when they fire, a webhook is created and called, and variables and secrets are set', async ({
  page,
}) => {
  await signIn(page);
  const folder = await page.request.post('/api/v1/folders', { data: { name: 'Config' }, headers: csrf });
  const folderId = ((await folder.json()) as { id: string }).id;
  const created = await page.request.post(`/api/v1/folders/${folderId}/automations`, {
    data: { name: 'deploy', yaml: file },
    headers: csrf,
  });
  expect(created.ok()).toBeTruthy();
  const id = ((await created.json()) as { id: string }).id;

  // The schedule with its next time; the webhook without an address yet.
  await page.goto(`/automations/${id}/triggers`);
  const triggers = page.getByRole('list', { name: 'Triggers' });
  await expect(triggers.getByText('0 6 * * 1-5')).toBeVisible();
  await expect(triggers.getByText(/next: in /)).toBeVisible();

  // A signed address: shown once, with examples, and closed only once saved.
  await page.getByRole('button', { name: 'Create address' }).click();
  const dialog = page.getByRole('dialog', { name: 'Webhook address created' });
  await expect(dialog.getByRole('button', { name: 'Done' })).toBeDisabled();
  const address = (await dialog.getByLabel('Address', { exact: true }).textContent()) ?? '';
  const secret = (await dialog.getByLabel('Signing secret', { exact: true }).textContent()) ?? '';
  await expect(dialog.getByLabel('Example call with curl')).toContainText('X-AutoCanvas-Signature');
  await snapshot(page, 'triggers-1-webhook-created');
  await dialog.getByRole('button', { name: 'PowerShell' }).click();
  await expect(dialog.getByLabel('Example call with PowerShell')).toContainText('HMACSHA256');
  await dialog.getByLabel(/I have saved the address/).check();
  await dialog.getByRole('button', { name: 'Done' }).click();
  await expect(triggers.getByText('Signed calls only')).toBeVisible();

  // The address works with what the dialog showed: a signed call starts a run.
  const body = '{"reason":"a test call"}';
  const signature = `sha256=${createHmac('sha256', secret).update(body).digest('hex')}`;
  const call = await page.request.post(new URL(address).pathname, {
    data: body,
    headers: { 'Content-Type': 'application/json', 'X-AutoCanvas-Signature': signature },
  });
  expect(call.status()).toBe(202);
  const unsigned = await page.request.post(new URL(address).pathname, {
    data: body,
    headers: { 'Content-Type': 'application/json' },
  });
  expect(unsigned.ok()).toBeFalsy();
  await snapshot(page, 'triggers-2-tab');

  // A variable and a secret of the folder.
  await page.goto(`/folders/${folderId}/configuration`);
  await page.getByRole('button', { name: 'Add variable' }).click();
  let editing = page.getByRole('dialog', { name: 'Add a variable' });
  await editing.getByLabel('Name').fill('CHANNEL');
  await editing.getByLabel('Value').fill('stable');
  await editing.getByRole('button', { name: 'Add variable' }).click();
  await expect(page.getByRole('region', { name: 'Variables' }).getByText('stable')).toBeVisible();
  await page.getByRole('button', { name: 'Add secret' }).click();
  editing = page.getByRole('dialog', { name: 'Add a secret' });
  await editing.getByLabel('Name').fill('TOKEN');
  await editing.getByLabel('Value').fill('s3cret-value');
  await editing.getByRole('button', { name: 'Add secret' }).click();
  await expect(page.getByRole('region', { name: 'Secrets' }).getByText('TOKEN')).toBeVisible();
  await expect(page.getByText('s3cret-value')).toHaveCount(0);

  // An instance variable the folder overrides.
  await page.goto('/admin');
  await page.getByRole('button', { name: 'Add variable' }).click();
  editing = page.getByRole('dialog', { name: 'Add a variable' });
  await editing.getByLabel('Name').fill('CHANNEL');
  await editing.getByLabel('Value').fill('beta');
  await editing.getByRole('button', { name: 'Add variable' }).click();
  await expect(page.getByRole('region', { name: 'Variables' }).getByText('beta')).toBeVisible();

  // What a run reads, in one list per kind: each value with where it is defined.
  await page.goto(`/automations/${id}/configuration`);
  const variables = page.getByRole('list', { name: 'Variables' });
  const channel = variables.getByRole('listitem').filter({ hasText: 'CHANNEL' });
  await expect(channel.getByText('stable')).toBeVisible();
  await expect(channel.getByRole('link', { name: 'folder Config' })).toBeVisible();
  await expect(variables.getByRole('listitem').filter({ hasText: 'REGION' }).getByText('in the file')).toBeVisible();
  await expect(page.getByRole('list', { name: 'Secrets' }).getByRole('link', { name: 'folder Config' })).toBeVisible();
  await channel.getByRole('button', { name: 'Every definition of CHANNEL' }).click();
  await expect(page.getByRole('list', { name: 'Definitions of CHANNEL, from the instance down' })).toContainText(
    'beta',
  );
  await snapshot(page, 'configuration-1-automation');
});
