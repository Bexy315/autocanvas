import { expect, test } from '@playwright/test';
import { GitHubStub } from './githubStub';
import { docsShot, signIn, snapshot } from './helpers';

const csrf = { 'X-AutoCanvas-Request': '1' };

test('an admin creates the GitHub App through the manifest flow, removes it, and enters it by hand', async ({
  page,
  baseURL,
}) => {
  const github = new GitHubStub(5096);
  await github.start();
  await signIn(page);
  // GitHub sends the browser back to the public address; the instance's own address serves here.
  const before = (await (await page.request.get('/api/v1/instance')).json()) as {
    publicAddress: { value: string | null };
    runnerAddress: { value: string | null };
  };
  await page.request.put('/api/v1/instance', {
    data: { publicAddress: baseURL, runnerAddress: before.runnerAddress.value },
    headers: csrf,
  });
  await page.request.delete('/api/v1/forges/github', { headers: csrf });

  try {
    await page.goto('/admin/forges');
    await expect(page.getByRole('heading', { name: 'No GitHub App yet' })).toBeVisible();
    await docsShot(page, 'github-create');

    // To "GitHub" with the manifest, and back with a code once the app is created there.
    await page.getByText('GitHub Enterprise Server', { exact: true }).click();
    await page.getByLabel('GitHub address').fill(github.web);
    await page.getByRole('button', { name: 'Create GitHub App' }).click();
    await page.getByRole('link', { name: 'Create GitHub App for acme' }).click();
    await expect(page.getByRole('link', { name: 'AutoCanvas (e2e)' })).toBeVisible();
    await expect(page).toHaveURL(/\/admin\/forges$/);
    await expect(page.getByRole('list', { name: 'Installations' })).toContainText('acme');
    await expect(page.getByText(`${baseURL}/api/v1/forges/github/events`)).toBeVisible();
    await docsShot(page, 'github-app');
    await snapshot(page, '13-forges-app');

    await page.getByRole('button', { name: 'Remove' }).click();
    await page.getByRole('button', { name: 'Remove app' }).click();
    await expect(page.getByRole('heading', { name: 'No GitHub App yet' })).toBeVisible();

    // The same app, entered by hand.
    await page.getByRole('button', { name: 'Enter an existing app' }).click();
    const dialog = page.getByRole('dialog', { name: 'Enter an existing GitHub App' });
    await dialog.getByLabel('App ID').fill(String(github.appId));
    await dialog.getByLabel('Private key').fill(github.privateKey);
    await dialog.getByLabel('Webhook secret').fill(github.webhookSecret);
    await dialog.getByLabel('GitHub address').fill(github.web);
    await snapshot(page, '13-forges-enter');
    await dialog.getByRole('button', { name: 'Enter app' }).click();
    await expect(page.getByRole('link', { name: 'AutoCanvas (e2e)' })).toBeVisible();
  } finally {
    await page.request.delete('/api/v1/forges/github', { headers: csrf });
    await page.request.put('/api/v1/instance', {
      data: { publicAddress: before.publicAddress.value, runnerAddress: before.runnerAddress.value },
      headers: csrf,
    });
    await github.stop();
  }
});
