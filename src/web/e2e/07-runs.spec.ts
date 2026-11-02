import { expect, test, type Page } from '@playwright/test';
import { signIn, snapshot } from './helpers';

const file = `version: 1
name: Nightly
triggers:
  manual: true
jobs:
  build:
    runs_on: linux
    steps:
      - name: Install
        run: npm ci
      - name: Test
        run: npm test
  deploy:
    needs: [build]
    runs_on: linux
    steps:
      - run: ./deploy
`;

const csrf = { 'X-AutoCanvas-Request': '1' };
const esc = '\u001B';

const line = (text: string, source = 'Stdout') => ({ time: '2026-11-02T10:00:00Z', source, text });

/** What a runner would have written for `build`: two steps, colors, an error on stderr. */
function fakeLog(attemptId: string) {
  return {
    attemptId,
    chunks: [
      { sequence: 1, stepIndex: null, lines: [line('Runner runner-1 picked up the job', 'System')] },
      {
        sequence: 2,
        stepIndex: 0,
        lines: [line('added 312 packages in 4s'), line(`${esc}[32m✓${esc}[0m dependencies are up to date`)],
      },
      {
        sequence: 3,
        stepIndex: 1,
        lines: [
          line(`${esc}[1m> jest --ci${esc}[0m`),
          line(`${esc}[42m${esc}[30m PASS ${esc}[0m src/cart.test.ts`),
          line(`${esc}[41m${esc}[97m FAIL ${esc}[0m src/checkout.test.ts`),
          line(`  ${esc}[31m● checkout › applies the discount${esc}[0m`, 'Stderr'),
          line(`    Expected: ${esc}[32m90${esc}[0m`),
          line(`    Received: ${esc}[31m100${esc}[0m`),
          line(`Tests: ${esc}[1m${esc}[31m1 failed${esc}[0m, ${esc}[1m${esc}[32m41 passed${esc}[0m, 42 total`),
        ],
      },
    ],
    next: 3,
    missing: [],
    isComplete: true,
    isTruncated: false,
  };
}

/** Serves `build` as a finished, failed job with its log: no runner is part of these tests. */
async function serveFinishedBuild(page: Page, runId: string) {
  const attemptId = '01a19d00-0000-7000-8000-000000000001';
  await page.route(`**/api/v1/runs/${runId}/jobs/build`, (route) =>
    route.fulfill({
      json: {
        key: 'build',
        name: 'build',
        status: 'Failed',
        reason: null,
        outputs: {},
        startedAt: '2026-11-02T10:00:00Z',
        finishedAt: '2026-11-02T10:00:42Z',
        attempts: [
          {
            id: attemptId,
            number: 1,
            status: 'Failed',
            runnerId: null,
            runnerName: 'runner-1',
            queuedAt: '2026-11-02T10:00:00Z',
            startedAt: '2026-11-02T10:00:00Z',
            finishedAt: '2026-11-02T10:00:42Z',
            error: null,
            warnings: [],
            reports: [],
            steps: [
              {
                index: 0,
                stepId: null,
                name: 'Install',
                status: 'Succeeded',
                reason: null,
                exitCode: 0,
                outputs: {},
                error: null,
                startedAt: '2026-11-02T10:00:00Z',
                finishedAt: '2026-11-02T10:00:04Z',
              },
              {
                index: 1,
                stepId: null,
                name: 'Test',
                status: 'Failed',
                reason: null,
                exitCode: 1,
                outputs: {},
                error: null,
                startedAt: '2026-11-02T10:00:04Z',
                finishedAt: '2026-11-02T10:00:42Z',
              },
            ],
          },
        ],
      },
    }),
  );
  await page.route(`**/api/v1/runs/${runId}/attempts/${attemptId}/log**`, (route) =>
    route.fulfill({ json: fakeLog(attemptId) }),
  );
}

test('a run is started by hand, followed, found in the lists and cancelled', async ({ page }) => {
  await signIn(page);
  const folder = await page.request.post('/api/v1/folders', { data: { name: 'Runs' }, headers: csrf });
  const folderId = ((await folder.json()) as { id: string }).id;
  const created = await page.request.post(`/api/v1/folders/${folderId}/automations`, {
    data: { name: 'nightly', yaml: file },
    headers: csrf,
  });
  expect(created.ok()).toBeTruthy();
  const id = ((await created.json()) as { id: string }).id;

  // Started from the automation's Runs tab, the run opens: its first job waits for a runner.
  await page.goto(`/automations/${id}/runs`);
  await expect(page.getByRole('heading', { name: 'No runs yet' })).toBeVisible();
  await page.getByRole('button', { name: 'Run', exact: true }).first().click();
  await expect(page.getByRole('heading', { name: 'Run #1' })).toBeVisible();
  await expect(page.getByText('Running', { exact: true }).first()).toBeVisible();
  await expect(
    page
      .getByRole('region', { name: 'Jobs of the run' })
      .getByText(/^Running for/)
      .first(),
  ).toBeVisible();
  await expect(page.getByText('Waiting for a runner')).toBeVisible();
  await snapshot(page, 'runs-1-running');

  // The lists show it.
  await page.goto('/runs');
  await expect(page.getByRole('link', { name: 'Nightly run 1, Running' })).toBeVisible();
  await snapshot(page, 'runs-2-all-runs');
  await page.goto(`/automations/${id}/runs`);
  await page.getByRole('link', { name: 'run 1, Running' }).click();

  // Cancelled, it says so, and the list follows.
  await page.getByRole('button', { name: 'Cancel' }).click();
  await expect(page.getByText('Cancelled', { exact: true }).first()).toBeVisible();
  const runId = new URL(page.url()).pathname.split('/').at(-1) ?? '';

  // A job's steps and log (served by the test, as no runner takes part): colors, steps, search.
  await serveFinishedBuild(page, runId);
  await page.reload();
  const log = page.getByRole('log', { name: 'Lines of build, attempt 1' });
  await expect(log).toContainText('src/checkout.test.ts');
  await expect(log.getByText('PASS', { exact: true })).toHaveCSS('background-color', 'rgb(23, 112, 63)');
  await page.getByRole('searchbox', { name: 'Search the log' }).fill('received');
  await expect(page.getByText('1 of 1')).toBeVisible();
  await snapshot(page, 'runs-3-log');
  await page.getByRole('button', { name: 'Test' }).first().click();
  await page.getByRole('button', { name: /^2\. Test/ }).click();
  await expect(log).not.toContainText('src/checkout.test.ts');
});
