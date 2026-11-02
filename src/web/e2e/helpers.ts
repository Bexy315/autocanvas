import { expect, type Cookie, type Page } from '@playwright/test';
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

/**
 * Pictures for the user guide, of what is in view, light and dark, in `test-results/docs/`;
 * `npm run docs:screenshots` copies them to `docs/user/img/`.
 */
export async function docsShot(page: Page, name: string) {
  await picture(page, name, 'light');
  await picture(page, name, 'dark');
}

async function picture(page: Page, name: string, scheme: 'light' | 'dark') {
  await page.evaluate((value) => {
    document.documentElement.dataset['theme'] = value;
  }, scheme);
  await page.screenshot({ path: `test-results/docs/${name}-${scheme}.png`, animations: 'disabled', caret: 'hide' });
}

/** The admin's session after the first sign-in, kept for the specs that follow (one worker runs them all). */
let session: Cookie[] | null = null;

/**
 * Signs in as the first admin, after the smoke test chose the admin's own password. The session of
 * the first sign-in is reused: the server allows only a few sign-ins a minute from one address.
 */
export async function signIn(page: Page) {
  if (session) {
    await page.context().addCookies(session);
    await page.goto('/');
    // Waits for the start page; a session that ended shows the sign-in page instead.
    const signedIn = await page
      .getByRole('heading', { name: 'Automations', level: 1 })
      .waitFor({ state: 'visible', timeout: 5000 })
      .then(
        () => true,
        () => false,
      );
    if (signedIn) {
      return;
    }
  }
  await page.goto('/sign-in');
  await page.getByLabel('User name').fill(adminUserName);
  await page.getByLabel('Password').fill(newPassword);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: 'Automations', level: 1 })).toBeVisible();
  session = await page.context().cookies();
}
