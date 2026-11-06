import { createHmac } from 'node:crypto';
import { expect, test, type Locator, type Page } from '@playwright/test';
import { docsShot, signIn, snapshot } from './helpers';
import { dockerAvailable, startRunner, type Runner } from './runner';

/*
 * The whole way on the real server with the reference runner and Docker (the S3 walk-through):
 * an automation built on the canvas is saved, started and followed live; its tests and coverage are
 * read, a signed webhook starts it again, and the folder is shared with a group. The pictures of
 * the user guide come from here (`npm run docs:screenshots`).
 */

const csrf = { 'X-AutoCanvas-Request': '1' };

/** Writes a JUnit file with a failing test and a Cobertura file, slowly enough to follow live. */
const testScript = `for part in cart checkout payments; do echo "Testing $part"; sleep 1; done
cat > TEST-shop.xml <<'EOF'
<testsuite name="shop" tests="3" failures="1">
  <testcase classname="Shop.Cart" name="adds_an_item" time="0.012"/>
  <testcase classname="Shop.Cart" name="applies_the_discount" time="0.020">
    <failure message="Expected: 90, but was: 100">at Shop.Cart.ApplyDiscount() in Cart.cs:line 42
at Shop.Tests.CartTests.applies_the_discount() in CartTests.cs:line 17</failure>
  </testcase>
  <testcase classname="Shop.Checkout" name="pays" time="0.031"/>
</testsuite>
EOF
cat > coverage.xml <<'EOF'
<coverage line-rate="0.75" branch-rate="0" lines-covered="3" lines-valid="4" version="1" timestamp="0">
  <packages><package name="Shop"><classes>
    <class name="Shop.Cart" filename="Cart.cs" line-rate="0.75"><lines>
      <line number="40" hits="2"/><line number="41" hits="2"/><line number="42" hits="0"/><line number="43" hits="1"/>
    </lines></class>
  </classes></package></packages>
</coverage>
EOF
echo "Tests done"`;

const card = (page: Page, id: string) => page.locator(`.react-flow__node[data-id="${id}"]`);
const panel = (page: Page, title: string) => page.getByRole('complementary', { name: `Settings of ${title}` });
const file = (page: Page) => page.locator('.cm-content');

/** Opens a collapsed section of a panel. */
async function open(settings: Locator, title: string) {
  const header = settings.getByRole('button', { name: new RegExp(`^${title}`) });
  if ((await header.getAttribute('aria-expanded')) !== 'true') {
    await header.click();
  }
}

/** Drags a line from an output of one card to the input of another. */
async function wire(page: Page, from: string, to: string) {
  await page.locator('.react-flow__controls-fitview').click();
  await page.waitForTimeout(300);
  const a = await card(page, from).locator('.react-flow__handle.source[data-handleid="success"]').boundingBox();
  const b = await card(page, to).locator('.react-flow__handle.target').boundingBox();
  if (!a || !b) {
    throw new Error('Nothing to wire.');
  }
  await page.mouse.move(a.x + a.width / 2, a.y + a.height / 2);
  await page.mouse.down();
  await page.mouse.move(b.x + b.width / 2, b.y + b.height / 2, { steps: 12 });
  await page.mouse.up();
}

let runner: Runner | null = null;

test.beforeAll(async ({ browser }) => {
  // CI has Docker and must take this walk; a machine without it skips it.
  test.skip(!process.env['CI'] && !dockerAvailable(), 'Docker is not running.');
  const page = await browser.newPage();
  await signIn(page);
  runner = await startRunner(page.request, 'e2e-runner');
  await page.close();
});

test.afterAll(async () => {
  await runner?.stop();
});

test('an automation built on the canvas runs on a real runner, reports its tests and is shared', async ({ page }) => {
  test.setTimeout(240_000);
  await signIn(page);
  const group = await page.request.post('/api/v1/groups', { data: { name: 'shop-team' }, headers: csrf });
  expect(group.ok()).toBeTruthy();

  // A folder and an automation from the starter file.
  await page.getByRole('button', { name: 'New folder' }).first().click();
  await page.getByRole('textbox', { name: 'Name' }).fill('Shop');
  await page.getByRole('button', { name: 'Create folder' }).click();
  await expect(page.getByRole('heading', { name: 'Shop', level: 1 })).toBeVisible();
  await page.getByRole('button', { name: 'New automation' }).first().click();
  await page.getByRole('textbox', { name: 'Name' }).fill('checkout');
  await page.getByLabel('Message').fill('Started');
  await docsShot(page, 'tour-new-automation');
  await page.getByRole('button', { name: 'Create automation' }).click();
  await expect(page.getByRole('heading', { name: 'checkout', level: 1 })).toBeVisible();
  const automationId = new URL(page.url()).pathname.split('/').at(-1) ?? '';

  // On the canvas: the starter job becomes the tests, with a script and its reports.
  await page.getByRole('link', { name: 'Editor' }).click();
  await expect(card(page, 'hello')).toBeVisible();
  await card(page, 'hello').click();
  let settings = panel(page, 'hello');
  await settings.getByLabel('Id', { exact: true }).fill('test');
  await settings.getByLabel('Id', { exact: true }).press('Enter');
  settings = panel(page, 'test');
  await expect(settings).toBeVisible();
  await settings.getByRole('tab', { name: /^Steps/ }).click();
  await settings.getByRole('button', { name: 'Add step' }).click();
  await settings.getByRole('button', { name: 'Script', exact: true }).click();
  await settings.getByLabel('Script').fill(testScript);
  await expect(file(page)).toContainText('cat > TEST-shop.xml');
  await settings.getByRole('tab', { name: 'Job' }).click();
  await open(settings, 'Reports');
  await settings.getByRole('button', { name: 'Add a report' }).click();
  await settings.getByRole('button', { name: 'Add a report' }).click();
  const coverage = settings.getByRole('group', { name: 'Report 2' });
  await coverage.getByLabel('Format').selectOption('cobertura');
  await coverage.getByRole('button', { name: 'Remove **/TEST-*.xml' }).click();
  await coverage.getByLabel('Files').fill('coverage.xml');
  await coverage.getByLabel('Files').press('Enter');
  await expect(file(page)).toContainText('format: cobertura');
  await settings.press('Escape');

  // A second job after it, with a web server next to it and a node step.
  await page.getByRole('button', { name: /^Add Job:/ }).click();
  await expect(card(page, 'job')).toBeVisible();
  await card(page, 'job').click();
  settings = panel(page, 'job');
  await settings.getByLabel('Id', { exact: true }).fill('smoke');
  await settings.getByLabel('Id', { exact: true }).press('Enter');
  settings = panel(page, 'smoke');
  await open(settings, 'Services');
  await settings.getByRole('button', { name: 'Add a service' }).click();
  await settings.getByLabel('Name of the new service').fill('web');
  await settings.getByLabel('Name of the new service').press('Enter');
  const web = settings.getByRole('group', { name: 'web' });
  await web.getByLabel('From').selectOption('image');
  await web.getByLabel('Image').fill('nginx:1.29-alpine');
  await settings.getByRole('tab', { name: /^Steps/ }).click();
  await settings.getByRole('button', { name: 'Add step' }).click();
  await settings.getByRole('button', { name: 'Script', exact: true }).click();
  await settings.getByLabel('Script').fill('wget -qO- http://web/ | grep -o "Welcome to nginx"');
  await settings.getByRole('button', { name: 'All steps' }).click();
  await settings.getByRole('button', { name: 'Add step' }).click();
  await settings.getByRole('button', { name: 'http_request', exact: true }).click();
  // Node steps run on the runner itself, so it asks the server whether it is ready.
  await settings
    .getByRole('textbox', { name: 'url (required)', exact: true })
    .fill('http://127.0.0.1:5090/health/ready');
  await expect(file(page)).toContainText('image: nginx:1.29-alpine');
  await settings.press('Escape');
  // Laid out first: the new card was placed next to the first one.
  await page.getByRole('button', { name: 'Tidy up' }).click();
  await wire(page, 'test', 'smoke');
  await expect(file(page)).toContainText('needs: [test]');

  // The automation may be started by a webhook too.
  await page.locator('.react-flow__controls-fitview').click();
  await page.waitForTimeout(300);
  await card(page, 'trigger').click();
  await panel(page, 'Automation').getByLabel('By a webhook').check();
  await expect(file(page)).toContainText('webhook: true');
  await page.keyboard.press('Escape');
  await page.getByRole('button', { name: 'Tidy up' }).click();
  await page.waitForTimeout(400);
  await docsShot(page, 'tour-editor');
  // Only the canvas, with a job's settings open.
  await page.getByRole('group', { name: 'Show' }).getByRole('button', { name: 'Canvas', exact: true }).click();
  await page.locator('.react-flow__controls-fitview').click();
  await page.waitForTimeout(400);
  await card(page, 'smoke').click();
  await docsShot(page, 'tour-canvas');
  await page.keyboard.press('Escape');

  // Saved, then started.
  await page.keyboard.press('ControlOrMeta+s');
  await page.getByRole('dialog', { name: 'Save version 2' }).getByRole('textbox', { name: 'Message' }).fill('Tests');
  await page.keyboard.press('Enter');
  await expect(page.getByText('Saved version 2')).toBeVisible();
  await page.goto(`/automations/${automationId}/runs`);
  await page.getByRole('button', { name: 'Run', exact: true }).first().click();
  await expect(page.getByRole('heading', { name: 'Run #1' })).toBeVisible();

  // The log is followed while the job runs.
  const log = page.getByRole('log', { name: 'Lines of test, attempt 1' });
  await expect(log).toContainText('Testing cart', { timeout: 120_000 });
  await expect(log).toContainText('Tests done', { timeout: 30_000 });
  await docsShot(page, 'tour-run-log');
  await expect(page.getByText('Succeeded', { exact: true }).first()).toBeVisible({ timeout: 120_000 });
  await card(page, 'smoke').click();
  const smoke = page.getByRole('log', { name: 'Lines of smoke, attempt 1' });
  await expect(smoke).toContainText('Welcome to nginx');
  await snapshot(page, 'walk-1-run');

  // The runner that ran it, online, as admins see it.
  await page.goto('/admin/runners');
  await expect(page.getByText('Online').first()).toBeVisible();
  await docsShot(page, 'admin-runners');
  await page.goBack();

  // The failed test with its stack trace, and the coverage.
  const views = page.getByRole('navigation', { name: 'Views of the run' });
  await expect(views.getByRole('link', { name: /Tests 1 failed/ })).toBeVisible();
  await views.getByRole('link', { name: /Tests/ }).click();
  const tests = page.getByRole('list', { name: /^Tests of/ });
  await expect(tests.getByText('applies_the_discount', { exact: true })).toBeVisible();
  await expect(tests.getByText(/Cart\.cs:line 42/)).toBeVisible();
  await docsShot(page, 'tour-tests');
  await views.getByRole('link', { name: /Coverage 75 %/ }).click();
  await expect(page).toHaveURL(/view=coverage/);
  const packages = page.getByRole('list', { name: 'Packages of cobertura' });
  await packages.getByRole('button', { name: /^Shop/ }).first().click();
  await expect(packages.getByText('Shop.Cart', { exact: true })).toBeVisible();
  await expect(packages.getByText('42', { exact: false }).first()).toBeVisible();
  await docsShot(page, 'tour-coverage');

  // A signed webhook starts it again.
  await page.goto(`/automations/${automationId}/triggers`);
  await page.getByRole('button', { name: 'Create address' }).click();
  const dialog = page.getByRole('dialog', { name: 'Webhook address created' });
  const address = (await dialog.getByLabel('Address', { exact: true }).textContent()) ?? '';
  const secret = (await dialog.getByLabel('Signing secret', { exact: true }).textContent()) ?? '';
  await docsShot(page, 'tour-webhook');
  await dialog.getByLabel(/I have saved the address/).check();
  await dialog.getByRole('button', { name: 'Done' }).click();
  const body = '{"reason":"walk-through"}';
  const call = await page.request.post(new URL(address).pathname, {
    data: body,
    headers: {
      'Content-Type': 'application/json',
      'X-AutoCanvas-Signature': `sha256=${createHmac('sha256', secret).update(body).digest('hex')}`,
    },
  });
  expect(call.status()).toBe(202);
  await page.goto(`/automations/${automationId}/runs`);
  // Waited for, so the runner stops with nothing left running.
  await expect(page.getByRole('link', { name: /run 2, Succeeded/ })).toBeVisible({ timeout: 120_000 });
  await docsShot(page, 'tour-runs');

  // The folder shared with the team.
  await page.getByRole('navigation', { name: 'Breadcrumbs' }).getByRole('link', { name: 'Shop' }).click();
  await page.getByRole('link', { name: 'Sharing' }).click();
  await page.getByRole('button', { name: 'Share', exact: true }).click();
  const share = page.getByRole('dialog', { name: 'Share Shop' });
  await share.getByLabel('Person or group').fill('shop');
  await share.getByRole('button', { name: /^shop-team/ }).click();
  await share.getByRole('button', { name: 'Share', exact: true }).click();
  await expect(page.getByRole('list', { name: 'Roles given here' })).toContainText('shop-team');
  await docsShot(page, 'tour-sharing');
  await page.getByRole('link', { name: 'Contents' }).click();
  await docsShot(page, 'tour-folder');
});
