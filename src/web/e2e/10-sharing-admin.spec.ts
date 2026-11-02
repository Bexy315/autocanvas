import { expect, test } from '@playwright/test';
import { signIn, snapshot } from './helpers';

const csrf = { 'X-AutoCanvas-Request': '1' };
const file = `version: 1
name: Report
triggers:
  manual: true
jobs:
  report:
    runs_on: linux
    steps:
      - run: echo report
`;

test('an admin adds a person, shares a folder with them, and they see it; runners and tokens are set up', async ({
  page,
  browser,
}) => {
  await signIn(page);
  const folder = await page.request.post('/api/v1/folders', { data: { name: 'Shared' }, headers: csrf });
  const folderId = ((await folder.json()) as { id: string }).id;
  await page.request.post(`/api/v1/folders/${folderId}/automations`, {
    data: { name: 'report', yaml: file },
    headers: csrf,
  });

  // A new user, with a first password made up by the browser and shown once.
  await page.goto('/admin/users');
  await page.getByRole('button', { name: 'New user' }).click();
  const create = page.getByRole('dialog', { name: 'New user' });
  await create.getByLabel('User name').fill('vic');
  await create.getByLabel('Display name').fill('Vic Viewer');
  await create.getByRole('button', { name: 'Create user' }).click();
  const shown = page.getByRole('dialog', { name: 'Password of vic' });
  const password = (await shown.getByLabel('Password', { exact: true }).textContent()) ?? '';
  expect(password).toHaveLength(20);
  await snapshot(page, 'admin-1-new-user');
  await shown.getByLabel('I have passed the password on').check();
  await shown.getByRole('button', { name: 'Done' }).click();
  await expect(page.getByRole('list', { name: 'Users' })).toContainText('Vic Viewer');

  // A group, with vic in it.
  await page.goto('/admin/groups');
  await page.getByRole('button', { name: 'New group' }).click();
  await page.getByRole('dialog', { name: 'New group' }).getByLabel('Name').fill('readers');
  await page.getByRole('button', { name: 'Create group' }).click();
  await page.getByRole('list', { name: 'Groups' }).getByRole('button', { name: 'readers', exact: true }).click();
  const members = page.getByRole('dialog', { name: 'Members of readers' });
  await members.getByLabel('Add a person').fill('vic');
  await members.getByRole('button', { name: /^vic/ }).click();
  await expect(members.getByRole('list', { name: 'Members' })).toContainText('vic');
  await members.getByRole('button', { name: 'Close' }).first().click();

  // The folder shared with the group, as Viewer.
  await page.goto(`/folders/${folderId}/sharing`);
  await page.getByRole('button', { name: 'Share', exact: true }).click();
  const share = page.getByRole('dialog', { name: 'Share Shared' });
  await share.getByLabel('Person or group').fill('read');
  await share.getByRole('button', { name: /^readers/ }).click();
  await share.getByRole('button', { name: 'Share', exact: true }).click();
  await expect(page.getByRole('list', { name: 'Roles given here' })).toContainText('readers');
  await snapshot(page, 'sharing-1-folder');

  // Vic signs in elsewhere, chooses a password, and sees the folder, but cannot start runs.
  const elsewhere = await browser.newContext();
  const vic = await elsewhere.newPage();
  await vic.goto('/sign-in');
  await vic.getByLabel('User name').fill('vic');
  await vic.getByLabel('Password').fill(password);
  await vic.getByRole('button', { name: 'Sign in' }).click();
  await expect(vic.getByRole('heading', { name: 'Choose your own password' })).toBeVisible();
  await vic.getByLabel('Current password').fill(password);
  await vic.getByLabel('New password', { exact: true }).fill('vic has a password now');
  await vic.getByLabel('Repeat the new password').fill('vic has a password now');
  await vic.getByRole('button', { name: 'Change password and continue' }).click();
  await expect(vic.getByRole('heading', { name: 'Automations', level: 1 })).toBeVisible();
  await vic.goto(`/folders/${folderId}`);
  await expect(vic.getByRole('heading', { name: 'Shared', level: 1 })).toBeVisible();
  await vic
    .getByRole('link', { name: /report/ })
    .first()
    .click();
  await vic.getByRole('link', { name: 'Runs' }).click();
  await expect(vic.getByRole('button', { name: 'Run', exact: true })).toHaveCount(0);
  await elsewhere.close();

  // A runner token with the commands that use it.
  await page.goto('/admin/runners');
  await page.getByRole('button', { name: 'Register a runner' }).click();
  await page.getByRole('dialog', { name: 'Register a runner' }).getByRole('button', { name: 'Create token' }).click();
  const runnerToken = page.getByRole('dialog', { name: 'Runner token created' });
  await expect(runnerToken.getByText(/register --token acrt_/).first()).toBeVisible();
  await snapshot(page, 'admin-2-runner-token');
  await runnerToken.getByLabel('I have used or saved the token').check();
  await runnerToken.getByRole('button', { name: 'Done' }).click();

  // An API token of one's own works at once.
  await page.goto('/account');
  await page.getByRole('button', { name: 'New token' }).click();
  await page.getByRole('dialog', { name: 'New API token' }).getByLabel('What it is for').fill('e2e');
  await page.getByRole('button', { name: 'Create token' }).click();
  const tokenDialog = page.getByRole('dialog', { name: 'API token created' });
  const token = (await tokenDialog.getByLabel('Token', { exact: true }).textContent()) ?? '';
  const me = await page.request.get('/api/v1/me', { headers: { Authorization: `Bearer ${token}` } });
  expect(me.ok()).toBeTruthy();
  await tokenDialog.getByLabel('I have saved the token').check();
  await tokenDialog.getByRole('button', { name: 'Done' }).click();
  await expect(page.getByRole('list', { name: 'API tokens' })).toContainText('e2e');
  await expect(page.getByRole('list', { name: 'Sessions' })).toContainText('This browser');
  await snapshot(page, 'account-1-tokens');
});
