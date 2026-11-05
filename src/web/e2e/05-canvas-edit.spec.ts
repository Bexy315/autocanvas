import { expect, test, type Locator, type Page } from '@playwright/test';
import { signIn, snapshot } from './helpers';

const start = `# Built on the canvas
version: 1
name: Shop
triggers:
  manual: true
jobs:
  build:   # compiles everything
    runs_on: linux
    image: alpine:3.22
    steps:
    - run: make
`;

const csrf = { 'X-AutoCanvas-Request': '1' };

const card = (page: Page, id: string) => page.locator(`.react-flow__node[data-id="${id}"]`);

/** Drags a line from an output of one card to the input of another. */
async function wire(page: Page, from: string, output: string, to: string) {
  // The view follows the last change; fitted first, both cards are in sight.
  await page.locator('.react-flow__controls-fitview').click();
  await page.waitForTimeout(300);
  const source = card(page, from).locator(`.react-flow__handle.source[data-handleid="${output}"]`);
  const target = card(page, to).locator('.react-flow__handle.target');
  await drag(page, source, target);
}

/** Drags from the middle of one element to a point of another, given as fractions of its size. */
async function drag(page: Page, from: Locator, to: Locator, at = { x: 0.5, y: 0.5 }) {
  const a = await from.boundingBox();
  const b = await to.boundingBox();
  if (!a || !b) {
    throw new Error('Nothing to drag.');
  }
  await page.mouse.move(a.x + a.width / 2, a.y + a.height / 2);
  await page.mouse.down();
  await page.mouse.move(b.x + b.width * at.x, b.y + b.height * at.y, { steps: 12 });
  await page.mouse.up();
}

const file = (page: Page) => page.locator('.cm-content');

test('jobs are added, wired, moved, deleted and saved on the canvas, keeping the file as written', async ({ page }) => {
  await signIn(page);
  const folder = await page.request.post('/api/v1/folders', { data: { name: 'Canvas edit' }, headers: csrf });
  const folderId = ((await folder.json()) as { id: string }).id;
  const created = await page.request.post(`/api/v1/folders/${folderId}/automations`, {
    data: { name: 'shop', yaml: start },
    headers: csrf,
  });
  const id = ((await created.json()) as { id: string }).id;
  await page.goto(`/automations/${id}/edit`);
  const canvas = page.getByRole('region', { name: 'Canvas', exact: true });
  await expect(card(page, 'build')).toBeVisible();
  // More room for the canvas: the separator moved with the keyboard.
  await page.getByRole('separator', { name: 'Resize canvas and file' }).press('End');

  // A job by choosing it in the palette, and a node type by dragging it onto the canvas.
  await canvas.getByRole('button', { name: /^Add Job:/ }).click();
  await expect(card(page, 'job')).toBeVisible();
  await expect(file(page)).toContainText('job:');
  // Onto an empty part of the canvas, away from the cards in the middle.
  await drag(page, canvas.getByRole('button', { name: /^Add http_request:/ }), canvas.locator('.react-flow__pane'), {
    x: 0.8,
    y: 0.85,
  });
  await expect(card(page, 'http-request')).toBeVisible();
  await expect(file(page)).toContainText('type: http_request');

  // Lines: on success, and from the red output on failure.
  await wire(page, 'build', 'success', 'job');
  await expect(file(page)).toContainText('needs: [build]');
  await wire(page, 'build', 'failure', 'http-request');
  await expect(file(page)).toContainText('needs: [{job: build, on: failure}]');
  await expect(canvas.getByText('on failure')).toBeVisible();
  await snapshot(page, 'canvas-edit-1-wired');

  // Moving a card saves its position.
  const box = await card(page, 'job').boundingBox();
  if (!box) {
    throw new Error('No card.');
  }
  await page.mouse.move(box.x + 40, box.y + 12);
  await page.mouse.down();
  await page.mouse.move(box.x + 160, box.y + 40, { steps: 8 });
  await page.mouse.up();
  await expect(file(page)).toContainText(/job: \{x: -?\d+, y: -?\d+\}/);

  // Deleting a card removes it and what waited for it; undo brings it back, redo removes it again.
  await card(page, 'http-request').click();
  await page.keyboard.press('Delete');
  await expect(card(page, 'http-request')).toHaveCount(0);
  await expect(file(page)).not.toContainText('http_request');
  await canvas.getByRole('button', { name: 'Undo (Ctrl+Z)' }).click();
  await expect(card(page, 'http-request')).toBeVisible();
  await canvas.getByRole('button', { name: 'Redo (Ctrl+Y)' }).click();
  await expect(card(page, 'http-request')).toHaveCount(0);

  await canvas.getByRole('button', { name: 'Tidy up' }).click();
  await expect(file(page)).toContainText('trigger: {x:');
  // The view is fitted to the new layout.
  await page.waitForTimeout(400);

  // The hand-written parts are as they were.
  await expect(file(page)).toContainText('# Built on the canvas');
  await expect(file(page)).toContainText('build:   # compiles everything');
  await expect(file(page)).toContainText('    - run: make');
  await snapshot(page, 'canvas-edit-2-tidied');

  // Ctrl+S on the canvas asks for a message; Esc saves nothing, the second time Enter saves.
  await card(page, 'build').click();
  await page.keyboard.press('ControlOrMeta+s');
  await expect(page.getByRole('dialog', { name: 'Save version 2' })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await page.keyboard.press('ControlOrMeta+s');
  await page.getByRole('textbox', { name: 'Message' }).fill('Built on the canvas');
  await page.keyboard.press('Enter');
  await expect(page.getByText('Saved version 2')).toBeVisible();
  await page.reload();
  await expect(card(page, 'job')).toBeVisible();
  await expect(file(page)).toContainText('needs: [build]');
});
