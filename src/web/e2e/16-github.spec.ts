import { createHmac } from 'node:crypto';
import { expect, test } from '@playwright/test';
import { GitHubStub } from './githubStub';
import { GitServer } from './gitServer';
import { docsShot, signIn, snapshot } from './helpers';

const csrf = { 'X-AutoCanvas-Request': '1' };
const build = `version: 1
name: Build on GitHub
triggers:
  manual: true
  pull_request:
    branches: [main]
jobs:
  build:
    runs_on: linux
    steps:
      - run: make
`;

test('a GitHub repository is connected through the app; a pull request runs, reports a status and a comment, and an edit becomes a pull request', async ({
  page,
  baseURL,
}) => {
  test.setTimeout(180_000);
  const git = new GitServer(5099);
  const github = new GitHubStub(5096);
  await git.start();
  await github.start();
  git.create('acme/shop');
  git.commit('acme/shop', { '.autocanvas/automations/build.yml': build }, 'Add the build');
  github.repositories.push({ id: 9001, fullName: 'acme/shop', cloneUrl: git.url('acme/shop') });
  await signIn(page);
  const before = (await (await page.request.get('/api/v1/instance')).json()) as {
    publicAddress: { value: string | null };
    runnerAddress: { value: string | null };
  };
  let folderId = '';

  try {
    // The app, as an admin entered it (Administration → Forges, e2e/13), and the public address its links use.
    await page.request.put('/api/v1/instance', {
      data: { publicAddress: baseURL, runnerAddress: before.runnerAddress.value },
      headers: csrf,
    });
    await page.request.delete('/api/v1/forges/github', { headers: csrf });
    const app = await page.request.put('/api/v1/forges/github', {
      data: {
        appId: github.appId,
        privateKey: github.privateKey,
        webhookSecret: github.webhookSecret,
        webAddress: github.web,
      },
      headers: csrf,
    });
    expect(app.ok()).toBe(true);
    const folder = await page.request.post('/api/v1/folders', {
      data: { name: `Shop on GitHub ${Math.random().toString(36).slice(2, 5)}` },
      headers: csrf,
    });
    folderId = ((await folder.json()) as { id: string }).id;

    // Connected in the folder from the app's repositories.
    await page.goto(`/folders/${folderId}/repositories`);
    await page.getByRole('button', { name: 'Connect a repository' }).click();
    const connect = page.getByRole('dialog', { name: 'Connect a repository' });
    await expect(connect.getByRole('list', { name: 'Repositories on GitHub' })).toContainText('acme/shop');
    await docsShot(page, 'github-connect');
    await connect.getByRole('button', { name: 'Connect', exact: true }).click();
    const card = page.getByRole('list', { name: 'Repositories' }).getByRole('listitem').first();
    await expect(card.getByText('1 automation')).toBeVisible();
    await expect(card.getByText('GitHub', { exact: true })).toBeVisible();

    // A pull request: GitHub's signed event starts a run at its head.
    const head = git.commit('acme/shop', { 'src/app.c': 'int main;' }, 'Faster', 'feature');
    git.setRef('acme/shop', 'refs/pull/12/head', head);
    const event = JSON.stringify({
      action: 'opened',
      pull_request: {
        number: 12,
        title: 'Faster',
        html_url: `${github.web}/acme/shop/pull/12`,
        user: { login: 'octocat' },
        head: { ref: 'feature', sha: head, repo: { id: 9001 } },
        base: { ref: 'main', repo: { id: 9001 } },
      },
      repository: { id: 9001 },
    });
    const delivered = await page.request.post('/api/v1/forges/github/events', {
      data: event,
      headers: {
        'content-type': 'application/json',
        'X-GitHub-Event': 'pull_request',
        'X-GitHub-Delivery': `e2e-${Date.now()}`,
        'X-Hub-Signature-256': `sha256=${createHmac('sha256', github.webhookSecret).update(event).digest('hex')}`,
      },
    });
    expect(delivered.status()).toBe(200);
    const automations = (await (await page.request.get(`/api/v1/folders/${folderId}/automations`)).json()) as {
      items: { id: string }[];
    };
    const automationId = automations.items[0]?.id ?? '';
    const pullRun = async () => {
      const runs = (await (await page.request.get(`/api/v1/automations/${automationId}/runs`)).json()) as {
        items: { id: string; trigger: string }[];
      };
      return runs.items.find((run) => run.trigger === 'PullRequest')?.id ?? '';
    };
    await expect.poll(pullRun, { timeout: 30_000 }).not.toBe('');
    const runId = await pullRun();

    // Reported to GitHub: pending, then (cancelled here, no runner) an error and the comment.
    await expect
      .poll(() => github.statuses.some((s) => s.commit === head && s.state === 'pending'), { timeout: 30_000 })
      .toBe(true);
    await page.request.post(`/api/v1/runs/${runId}/cancel`, { headers: csrf });
    await expect
      .poll(() => github.statuses.some((s) => s.commit === head && s.state === 'error'), { timeout: 30_000 })
      .toBe(true);
    await expect
      .poll(() => [...github.comments.values()].find((c) => c.number === 12)?.body ?? '', { timeout: 30_000 })
      .toContain('build #1');
    await page.goto(`/runs/${runId}`);
    await expect(page.getByRole('link', { name: 'pull request #12' })).toBeVisible();
    await snapshot(page, '16-github-run');

    // An edit in the editor becomes a pull request on GitHub.
    await page.goto(`/automations/${automationId}/edit`);
    await page.getByRole('textbox', { name: 'File of Build on GitHub' }).click();
    await page.keyboard.press('ControlOrMeta+A');
    await page.keyboard.insertText(build.replace('run: make', 'run: make -j8'));
    await page.getByRole('button', { name: 'Propose' }).click();
    const propose = page.getByRole('dialog', { name: 'Propose as a pull request' });
    await propose.getByRole('textbox', { name: 'Title' }).fill('Build in parallel');
    await docsShot(page, 'github-propose');
    await propose.getByRole('button', { name: 'Open pull request' }).click();
    await expect(page.getByRole('link', { name: /Proposed as pull request #\d+/ })).toBeVisible();
    expect(github.pullRequests.at(-1)).toMatchObject({ title: 'Build in parallel', base: 'main' });
    expect(github.fileChanges.at(-1)?.content).toContain('make -j8');
    await snapshot(page, '16-github-proposed');
  } finally {
    // Leave no GitHub repository or app behind for later tests (e2e/13 removes the app first).
    const repositories = folderId
      ? ((await (await page.request.get(`/api/v1/folders/${folderId}/repositories`)).json()) as { id: string }[])
      : [];
    await Promise.all(
      repositories.map((repository) => page.request.delete(`/api/v1/repositories/${repository.id}`, { headers: csrf })),
    );
    await page.request.delete('/api/v1/forges/github', { headers: csrf });
    await page.request.put('/api/v1/instance', {
      data: { publicAddress: before.publicAddress.value, runnerAddress: before.runnerAddress.value },
      headers: csrf,
    });
    await github.stop();
    await git.stop();
  }
});
