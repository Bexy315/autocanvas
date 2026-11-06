import { expect, test, type Page } from '@playwright/test';
import { docsShot, signIn, snapshot } from './helpers';

const start = `# Built in the panel
version: 1
name: Site
triggers:
  manual: true
jobs:
  build:   # compiles the site
    runs_on: linux
    image: alpine:3.22
    steps:
    - run: make
      id: make
    outputs:
      version: \${{ steps.make.outputs.version }}
  deploy:
    needs: [build]
    runs_on: linux
    image: alpine:3.22
    steps:
    - run: ./deploy \${{ jobs.build.outputs.version }}
`;

const csrf = { 'X-AutoCanvas-Request': '1' };
const card = (page: Page, id: string) => page.locator(`.react-flow__node[data-id="${id}"]`);
const file = (page: Page) => page.locator('.cm-content');
const panel = (page: Page, title: string) => page.getByRole('complementary', { name: `Settings of ${title}` });

test('a job, its steps and the triggers are set up in the side panel, keeping the file as written', async ({
  page,
}) => {
  await signIn(page);
  const folder = await page.request.post('/api/v1/folders', { data: { name: 'Panel' }, headers: csrf });
  const folderId = ((await folder.json()) as { id: string }).id;
  await page.request.put(`/api/v1/folders/${folderId}/secrets/DEPLOY_TOKEN`, { data: { value: 'e2e' }, headers: csrf });
  const created = await page.request.post(`/api/v1/folders/${folderId}/automations`, {
    data: { name: 'site', yaml: start },
    headers: csrf,
  });
  expect(created.ok()).toBeTruthy();
  const id = ((await created.json()) as { id: string }).id;
  await page.goto(`/automations/${id}/edit`);
  await expect(card(page, 'build')).toBeVisible();

  // Choosing a card opens its panel; a value typed there lands in the file at once.
  await card(page, 'build').click();
  const build = panel(page, 'build');
  await expect(build).toBeVisible();
  // The job's tab: sections fold away and say what they hold.
  await build.getByRole('button', { name: /^Runs on/ }).click();
  await build.getByLabel('Image').fill('node:24');
  await expect(file(page)).toContainText('image: node:24');

  // The steps' tab: a script step, written as a block, and a node type's step with its inputs.
  await build.getByRole('tab', { name: /^Steps/ }).click();
  await build.getByRole('button', { name: 'Add step' }).click();
  await build.getByRole('button', { name: 'Script', exact: true }).click();
  await build.getByLabel('Script').fill('npm ci\nnpm test');
  await expect(file(page)).toContainText('- run: |-');
  await build.getByRole('button', { name: 'All steps' }).click();
  await build.getByRole('button', { name: 'Add step' }).click();
  await build.getByRole('button', { name: 'http_request', exact: true }).click();
  await build.getByRole('textbox', { name: 'url (required)', exact: true }).fill('https://example.com/hook');
  await expect(file(page)).toContainText('url: https://example.com/hook');
  await snapshot(page, 'panel-1-step');
  await docsShot(page, 'nodes-http-request');

  // The second step goes first; then the job gets another id, and what names it follows.
  await build.getByRole('button', { name: 'All steps' }).click();
  await build.getByRole('button', { name: 'Move step 2 up' }).click();
  await snapshot(page, 'panel-1-steps');
  await build.getByRole('tab', { name: 'Job' }).click();
  await build.getByLabel('Id', { exact: true }).fill('compile');
  await build.getByLabel('Id', { exact: true }).press('Enter');
  await expect(file(page)).toContainText('needs: [compile]');
  await expect(file(page)).toContainText('jobs.compile.outputs.version');
  const compile = panel(page, 'compile');
  await expect(compile).toBeVisible();
  await snapshot(page, 'panel-2-job');

  // A secret of the folder goes into the environment from the menu, and shows as a chip.
  await compile.getByRole('button', { name: /^Environment/ }).click();
  await compile.getByRole('button', { name: 'Add name' }).click();
  await compile.getByLabel('New name').fill('TOKEN');
  await compile.getByLabel('New name').press('Enter');
  await compile.getByRole('button', { name: 'Insert a variable or secret into Value of TOKEN' }).click();
  await compile.getByRole('button', { name: 'secrets.DEPLOY_TOKEN' }).click();
  await expect(file(page)).toContainText('TOKEN: ${{ secrets.DEPLOY_TOKEN }}');
  await expect(compile.getByRole('button', { name: /^Value of TOKEN: secret DEPLOY_TOKEN/ })).toBeVisible();
  await compile.getByRole('button', { name: 'Insert a variable or secret into Value of TOKEN' }).click();
  await snapshot(page, 'panel-2-expressions');
  await docsShot(page, 'values-insert');
  await page.keyboard.press('Escape');

  // A job from the palette waits for another, chosen with the keyboard in its panel.
  await page.getByRole('button', { name: /^Add Job:/ }).click();
  await card(page, 'job').click();
  const job = panel(page, 'job');
  await job.getByRole('button', { name: /^Needs/ }).click();
  await job.getByRole('button', { name: 'Add a job to wait for' }).click();
  await job.getByLabel('Waits for job 1').selectOption('deploy');
  await job.getByLabel('Starts').selectOption('failure');
  await expect(file(page)).toContainText('needs: [{job: deploy, on: failure}]');
  await job.press('Escape');
  await expect(job).toHaveCount(0);

  // The trigger card opens the automation's settings: a schedule is added.
  // The view followed the chosen job; fitted, the trigger is in sight again.
  await page.locator('.react-flow__controls-fitview').click();
  await page.waitForTimeout(300);
  await card(page, 'trigger').click();
  const automation = panel(page, 'Automation');
  await automation.getByRole('button', { name: 'Add a schedule' }).click();
  await automation.getByLabel('Cron').fill('30 5 * * 1-5');
  await expect(file(page)).toContainText('cron: 30 5 * * 1-5');
  await snapshot(page, 'panel-3-automation');

  // The hand-written parts are as they were; the file is saved as version 2.
  await expect(file(page)).toContainText('# Built in the panel');
  await expect(file(page)).toContainText('compile:   # compiles the site');
  await page.keyboard.press('ControlOrMeta+s');
  await page.getByRole('dialog', { name: 'Save version 2' }).getByRole('textbox', { name: 'Message' }).fill('Panel');
  await page.keyboard.press('Enter');
  await expect(page.getByText('Saved version 2')).toBeVisible();
});
