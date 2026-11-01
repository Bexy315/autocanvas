import { expect, type Page } from '@playwright/test';
import { adminUserName } from '../playwright.config';

/** The password the smoke test (01) gives the first admin; later specs sign in with it. */
export const newPassword = 'a password of my own';

/** Screenshots for review, in light and dark (CI keeps them as the `web-screenshots` artifact). */
export async function snapshot(page: Page, name: string) {
  await shoot(page, name, 'light');
  await shoot(page, name, 'dark');
}

async function shoot(page: Page, name: string, scheme: 'light' | 'dark') {
  // The page's code runs in the browser (DOM types come from tsconfig.node.json for that reason).
  await page.evaluate((value) => {
    document.documentElement.dataset['theme'] = value;
  }, scheme);
  // Transitions (e.g. button colors) finish first, so a shot never shows half the old scheme.
  await page.screenshot({
    path: `test-results/screenshots/${name}-${scheme}.png`,
    fullPage: true,
    animations: 'disabled',
  });
}

/** Signs in as the first admin, after the smoke test chose the admin's own password. */
export async function signIn(page: Page) {
  await page.goto('/sign-in');
  await page.getByLabel('User name').fill(adminUserName);
  await page.getByLabel('Password').fill(newPassword);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: 'Automations', level: 1 })).toBeVisible();
}
