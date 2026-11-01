import { expect, test, type Page } from '@playwright/test';
import { adminPassword, adminUserName } from '../playwright.config';

const newPassword = 'a password of my own';

/** Screenshots for review, in light and dark (CI keeps them as the `web-screenshots` artifact). */
async function snapshot(page: Page, name: string) {
  await shoot(page, name, 'light');
  await shoot(page, name, 'dark');
}

async function shoot(page: Page, name: string, scheme: 'light' | 'dark') {
  // The page's code runs in the browser (DOM types come from tsconfig.node.json for that reason).
  await page.evaluate((value) => {
    document.documentElement.dataset['theme'] = value;
  }, scheme);
  await page.screenshot({ path: `test-results/screenshots/${name}-${scheme}.png`, fullPage: true });
}

test('the first admin signs in, replaces the password, uses the shell and signs out', async ({ page }) => {
  await page.goto('/account');

  // Without a session the UI asks to sign in, and comes back afterwards.
  await expect(page).toHaveURL(/\/sign-in\?returnTo=%2Faccount$/);
  await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
  await snapshot(page, '1-sign-in');

  await page.getByLabel('User name').fill(adminUserName);
  await page.getByLabel('Password').fill('wrong password');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('alert')).toContainText('wrong');
  await snapshot(page, '2-sign-in-failed');

  await page.getByLabel('Password').fill(adminPassword);
  await page.getByRole('button', { name: 'Sign in' }).click();

  // The password someone else set must be replaced before anything else.
  await expect(page.getByRole('heading', { name: 'Choose your own password' })).toBeVisible();
  await page.getByLabel('Current password').fill(adminPassword);
  await page.getByLabel('New password', { exact: true }).fill('short');
  await page.getByLabel('Repeat the new password').fill('short');
  await page.getByRole('button', { name: 'Change password and continue' }).click();
  await expect(page.getByText(/at least 12 characters|12 to 256/i).last()).toBeVisible();
  await snapshot(page, '3-change-password');

  await page.getByLabel('New password', { exact: true }).fill(newPassword);
  await page.getByLabel('Repeat the new password').fill(newPassword);
  await page.getByRole('button', { name: 'Change password and continue' }).click();

  await expect(page).toHaveURL(/\/account$/);
  await expect(page.getByRole('heading', { name: 'Account' })).toBeVisible();
  await expect(page.getByRole('navigation', { name: 'Breadcrumbs' })).toContainText('Account');
  await snapshot(page, '4-account');

  await page.getByRole('link', { name: 'Automations' }).click();
  await expect(page.getByRole('heading', { name: 'Automations' })).toBeVisible();
  await snapshot(page, '5-shell');

  await page.getByRole('button', { name: 'Collapse sidebar' }).click();
  await expect(page.getByRole('button', { name: 'Expand sidebar' })).toBeVisible();
  await snapshot(page, '6-shell-collapsed');
  await page.getByRole('button', { name: 'Expand sidebar' }).click();

  // The color scheme is picked in the user menu and kept across reloads.
  await page.getByRole('button', { name: /Account menu/ }).click();
  await snapshot(page, '7-user-menu');
  await page.getByRole('menuitemradio', { name: 'Dark' }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');

  await page.getByRole('button', { name: /Account menu/ }).click();
  await page.getByRole('menuitem', { name: 'Sign out' }).click();
  await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();

  await page.goto('/');
  await expect(page).toHaveURL(/\/sign-in$/);
});

test('an unknown page says so inside the shell', async ({ page }) => {
  await page.goto('/sign-in');
  await page.getByLabel('User name').fill(adminUserName);
  await page.getByLabel('Password').fill(newPassword);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: 'Automations' })).toBeVisible();

  await page.goto('/no/such/page');

  await expect(page.getByRole('heading', { name: 'There is no page here' })).toBeVisible();
  await snapshot(page, '8-not-found');
});
