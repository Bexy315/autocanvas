import { expect, test, type Page } from '@playwright/test';
import { signIn, snapshot } from './helpers';

const release = `version: 1
name: Release
triggers:
  manual: true
  schedule:
    - cron: '0 3 * * 1-5'
      timezone: Europe/Berlin
jobs:
  build:
    runs_on: linux
    image: alpine:3.22
    steps:
      - name: Compile
        run: make
      - run: make test
  check:
    needs: [build]
    runs_on: linux
    image: alpine:3.22
    steps:
      - id: probe
        type: http_request
        with:
          url: https://example.com/health
    outputs:
      healthy: \${{ steps.probe.outputs.status }}
  route:
    type: switch
    needs: [check]
    expression: \${{ jobs.check.outputs.healthy }}
    cases:
      - name: ship
        when: '200'
      - name: hold
        default: true
  deploy:
    needs: [{ job: route, case: ship }]
    runs_on: linux
    image: alpine:3.22
    steps:
      - run: ./deploy
  alert:
    needs: [{ job: build, on: failure }]
    runs_on: linux
    image: alpine:3.22
    steps:
      - run: ./page-someone
  report:
    finally: true
    runs_on: linux
    image: alpine:3.22
    steps:
      - run: ./report
`;

const csrf = { 'X-AutoCanvas-Request': '1' };

async function replaceAll(page: Page, text: string) {
  await page.getByRole('textbox', { name: 'File of Release' }).click();
  await page.keyboard.press('ControlOrMeta+A');
  await page.keyboard.insertText(text);
}

test('the canvas draws the automation next to its file and follows it while typing', async ({ page }) => {
  await signIn(page);
  const folder = await page.request.post('/api/v1/folders', { data: { name: 'Canvas' }, headers: csrf });
  const folderId = ((await folder.json()) as { id: string }).id;
  const created = await page.request.post(`/api/v1/folders/${folderId}/automations`, {
    data: { name: 'release', yaml: release },
    headers: csrf,
  });
  const id = ((await created.json()) as { id: string }).id;

  await page.goto(`/automations/${id}/edit`);
  const canvas = page.getByRole('region', { name: 'Canvas' });
  await expect(canvas.getByText('Cron 0 3 * * 1-5 (Europe/Berlin)')).toBeVisible();
  await Promise.all(
    ['build', 'check', 'route', 'deploy', 'alert', 'report'].map((job) =>
      expect(canvas.locator(`.react-flow__node[data-id="${job}"]`)).toBeVisible(),
    ),
  );
  await expect(canvas.locator('.react-flow__edge')).toHaveCount(7);
  await expect(canvas.getByText('on failure')).toBeVisible();
  await snapshot(page, 'canvas-1-split');

  // A job added in the file appears on the canvas.
  await replaceAll(
    page,
    `${release}  smoke:\n    needs: [deploy]\n    runs_on: linux\n    steps:\n      - run: ./smoke\n`,
  );
  await expect(canvas.locator('.react-flow__node[data-id="smoke"]')).toBeVisible();

  // While the file cannot be read, the last readable state stays, marked as such.
  await replaceAll(page, `${release}  broken: [\n`);
  await expect(canvas.getByText(/^The file cannot be read right now;/)).toBeVisible();
  await snapshot(page, 'canvas-2-unreadable');

  // Choosing a card shows its job in the file.
  await replaceAll(page, release);
  await expect(canvas.getByText('The file cannot be read right now')).toBeHidden();
  await canvas.locator('.react-flow__node[data-id="deploy"]').click();
  await expect(page.locator('.cm-activeLine')).toContainText('deploy:');

  // The list shows the same, as text.
  await canvas.getByRole('button', { name: 'List' }).click();
  await expect(canvas.getByRole('list', { name: 'Jobs' })).toContainText('Needs route (ship).');
  await snapshot(page, 'canvas-3-list');
  await canvas.getByRole('button', { name: 'Graph' }).click();

  // Only the canvas, then only the file.
  const views = page.getByRole('group', { name: 'Show' });
  await views.getByRole('button', { name: 'Canvas', exact: true }).click();
  await expect(page.getByRole('region', { name: 'File' })).toBeHidden();
  await snapshot(page, 'canvas-4-canvas-only');
  await views.getByRole('button', { name: 'YAML', exact: true }).click();
  await expect(canvas).toBeHidden();
  await views.getByRole('button', { name: 'Split', exact: true }).click();
  await expect(canvas).toBeVisible();
});
