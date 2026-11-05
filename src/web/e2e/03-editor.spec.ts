import { expect, test, type Page } from '@playwright/test';
import { signIn, snapshot } from './helpers';

const start = `version: 1
name: Deploy
triggers:
  manual: true
jobs:
  build:
    runs_on: linux
    image: alpine:3.22
    steps:
      - run: echo build
`;

const fixed = `${start}  deploy:
    runs_on: linux
    image: alpine:3.22
    needs: [build]
    steps:
      - type: http_request
        with:
          url: https://example.com/deploy
          method: POST
`;

const csrf = { 'X-AutoCanvas-Request': '1' };

/** Replaces the editor's whole text, the way pasting would (typing Enter would add indentation). */
async function replaceAll(page: Page, text: string) {
  await page.getByRole('textbox', { name: 'File of Deploy' }).click();
  await page.keyboard.press('ControlOrMeta+A');
  await page.keyboard.insertText(text);
}

test('a file is edited with checks while typing, completed, saved, and saved again after a conflict', async ({
  page,
}) => {
  await signIn(page);
  const folder = await page.request.post('/api/v1/folders', { data: { name: 'Editing' }, headers: csrf });
  const folderId = ((await folder.json()) as { id: string }).id;
  const created = await page.request.post(`/api/v1/folders/${folderId}/automations`, {
    data: { name: 'deploy', yaml: start },
    headers: csrf,
  });
  const id = ((await created.json()) as { id: string }).id;

  await page.goto(`/automations/${id}`);
  await page.getByRole('link', { name: 'Editor' }).click();
  await expect(page.getByText('No problems found.')).toBeVisible();

  // A mistake is found by the server while typing, marked in the file and listed below it.
  await replaceAll(page, `${start}  deploy:\n    needs: [tset]\n    steps:\n      - run: echo deploy\n`);
  await expect(page.getByRole('list', { name: 'Problems in the file' })).toContainText('tset');
  await expect(page.locator('.cm-lintRange-error').first()).toBeVisible();
  await snapshot(page, 'editor-1-problem');

  // Completion offers the node types for a step's type.
  await replaceAll(page, `${start}      - type: h`);
  await expect(page.locator('.cm-tooltip-autocomplete')).toContainText('http_request');
  await snapshot(page, 'editor-2-completion');
  await page.keyboard.press('Escape');
  // Ctrl+I opens it too, where Ctrl+Space belongs to the system or the browser.
  await page.keyboard.press('ControlOrMeta+i');
  await expect(page.locator('.cm-tooltip-autocomplete')).toContainText('http_request');
  await page.keyboard.press('Escape');

  // Ctrl+F searches the file also while the focus is outside it, in the bar above.
  await page.getByRole('button', { name: 'Tidy up' }).focus();
  await page.keyboard.press('ControlOrMeta+f');
  await expect(page.getByRole('textbox', { name: 'Find' })).toBeFocused();
  await page.keyboard.insertText('type');
  await snapshot(page, 'editor-2-search');
  await page.keyboard.press('Escape');

  // A valid file is saved with a message as version 2: Ctrl+S asks for it, Enter saves.
  await replaceAll(page, fixed);
  await expect(page.getByText('No problems found.')).toBeVisible();
  await page.keyboard.press('ControlOrMeta+s');
  const saving = page.getByRole('dialog', { name: 'Save version 2' });
  await saving.getByRole('textbox', { name: 'Message' }).fill('Deploy after the build');
  await snapshot(page, 'editor-3-save-dialog');
  await page.keyboard.press('Enter');
  await expect(page.getByText('Saved version 2')).toBeVisible();
  await snapshot(page, 'editor-4-saved');

  // Someone else saves version 3 meanwhile; saving here shows the difference and saves on top.
  await replaceAll(page, `# Deploys after every build.\n${fixed}`);
  const other = await page.request.put(`/api/v1/automations/${id}`, {
    data: { yaml: fixed.replace('method: POST', 'method: PUT'), message: 'Use PUT' },
    headers: { ...csrf, 'If-Match': '"2"' },
  });
  expect(other.ok()).toBeTruthy();
  await page.getByRole('button', { name: 'Save' }).click();
  await page.getByRole('dialog', { name: 'Save version 3' }).getByRole('button', { name: 'Save version' }).click();
  const dialog = page.getByRole('dialog', { name: 'Someone saved in the meantime' });
  await expect(dialog.getByRole('region', { name: 'Your changes compared with version 3' })).toContainText(
    'Deploys after every build',
  );
  await snapshot(page, 'editor-5-conflict');
  await dialog.getByRole('button', { name: 'Save mine on top' }).click();
  await expect(page.getByText('Saved version 4')).toBeVisible();
});
