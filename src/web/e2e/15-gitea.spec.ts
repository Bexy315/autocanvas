import { expect, test, type Page } from '@playwright/test';
import { dockerHost, Gitea } from './gitea';
import { signIn, snapshot } from './helpers';
import { dockerAvailable, startRunner, type Runner } from './runner';

const csrf = { 'X-AutoCanvas-Request': '1' };

/** A run's jobs, their attempts' errors and logs, for the message of a failed assertion. */
async function runReport(page: Page, runId: string): Promise<string> {
  const run = (await (await page.request.get(`/api/v1/runs/${runId}`)).json()) as { jobs: { key: string }[] };
  const jobs = await Promise.all(
    run.jobs.map(
      async (job) =>
        (await (await page.request.get(`/api/v1/runs/${runId}/jobs/${job.key}`)).json()) as {
          key: string;
          attempts: { id: string; status: string; error: string | null }[];
        },
    ),
  );
  const attempts = jobs.flatMap((job) => job.attempts.map((attempt) => ({ job: job.key, ...attempt })));
  const logs = await Promise.all(
    attempts.map(async (attempt) => {
      const log = (await (
        await page.request.get(`/api/v1/runs/${runId}/attempts/${attempt.id}/log?limit=1000`)
      ).json()) as {
        chunks: { lines: { text: string }[] }[];
      };
      const lines = log.chunks.flatMap((chunk) => chunk.lines.map((line) => line.text)).join('\n');
      return `${attempt.job} (${attempt.status}): ${attempt.error ?? ''}\n${lines}`;
    }),
  );
  return logs.join('\n\n') || JSON.stringify(run.jobs);
}

/** Runs on pushes to main; its job checks out the pushed commit and shows the pushed file. */
const build = `version: 1
name: Build from Gitea
triggers:
  manual: true
  push:
    branches: [main]
jobs:
  build:
    runs_on: linux
    image: alpine:3.20
    steps:
      - run: test -f pushed.txt && cat pushed.txt
`;

test('a private Gitea repository is connected, a push through its webhook runs the pushed commit, checked out', async ({
  page,
}) => {
  // CI has Docker and must take this walk; a machine without it skips it.
  test.skip(!process.env['CI'] && !dockerAvailable(), 'Docker is not running.');
  test.setTimeout(420_000);
  const gitea = new Gitea(5098);
  let runner: Runner | null = null;
  try {
    await gitea.start();
    const repository = `shop${Math.random().toString(36).slice(2, 7)}`;
    await gitea.createRepository(repository);
    await gitea.commit(repository, '.autocanvas/automations/build.yml', build, 'Add the build');

    await signIn(page);
    runner = await startRunner(page.request, `gitea-${repository}`);
    const folder = await page.request.post('/api/v1/folders', { data: { name: `Gitea ${repository}` }, headers: csrf });
    const folderId = ((await folder.json()) as { id: string }).id;

    // Connected in the web UI with the token of a Gitea user: the repository is private.
    await page.goto(`/folders/${folderId}/repositories`);
    await page.getByRole('button', { name: 'Connect a repository' }).click();
    const connect = page.getByRole('dialog', { name: 'Connect a repository' });
    await connect.getByRole('button', { name: 'Git address' }).click();
    await connect.getByLabel('Address').fill(gitea.cloneUrl(repository));
    await connect.getByLabel('User name').fill(gitea.user);
    await connect.getByLabel('Token or password').fill(gitea.token);
    await connect.getByLabel('Poll every (minutes)').fill('0');
    await connect.getByRole('button', { name: 'Connect', exact: true }).click();
    const card = page.getByRole('list', { name: 'Repositories' }).getByRole('listitem').first();
    await expect(card.getByText('1 automation')).toBeVisible();

    // Its webhook address goes into Gitea, with the address Gitea reaches this server at.
    await page.getByRole('button', { name: `Actions for ${repository}` }).click();
    await page.getByRole('menuitem', { name: 'Create webhook address' }).click();
    const dialog = page.getByRole('dialog', { name: 'Webhook address' });
    const address = await dialog.locator('code').nth(0).innerText();
    const secret = await dialog.locator('code').nth(1).innerText();
    await dialog.getByLabel('I have entered them').check();
    await dialog.getByRole('button', { name: 'Done' }).click();
    const hook = new URL(address);
    await gitea.addWebhook(repository, `http://${dockerHost()}:${new URL(page.url()).port}${hook.pathname}`, secret);

    // A push: Gitea calls the webhook, and the automation runs at the pushed commit.
    const pushed = await gitea.commit(repository, 'pushed.txt', 'pushed through Gitea\n', 'Push a file');
    const automations = (await (await page.request.get(`/api/v1/folders/${folderId}/automations`)).json()) as {
      items: { id: string }[];
    };
    const automationId = automations.items[0]?.id ?? '';
    const finished = async () => {
      const runs = (await (await page.request.get(`/api/v1/automations/${automationId}/runs`)).json()) as {
        items: { id: string; state: string; trigger: string }[];
      };
      const run = runs.items.find((item) => item.trigger === 'Push');
      return run && run.state !== 'Running' ? run : null;
    };
    await expect.poll(async () => (await finished())?.state, { timeout: 240_000, intervals: [2000] }).toBeTruthy();
    const run = await finished();
    // A failed run says why in its attempts and logs; the CI output shows it.
    expect(run?.state, run?.state === 'Succeeded' ? '' : await runReport(page, run?.id ?? '')).toBe('Succeeded');
    const detail = (await (await page.request.get(`/api/v1/runs/${run?.id ?? ''}`)).json()) as {
      commit: string;
      ref: string;
    };
    expect(detail).toMatchObject({ commit: pushed, ref: 'refs/heads/main' });

    await page.goto(`/runs/${run?.id ?? ''}`);
    await expect(page.getByText(pushed.slice(0, 7))).toBeVisible();
    // Its only job is open, with the log of the step that read the pushed file.
    await expect(page.getByText('pushed through Gitea')).toBeVisible();
    await snapshot(page, '15-gitea-run');
  } finally {
    await runner?.stop();
    gitea.stop();
  }
});
