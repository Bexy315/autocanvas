import { act, screen, waitFor, within } from '@testing-library/react';
import { beforeAll, describe, expect, it } from 'vitest';
import type { components } from '../api/schema';
import { editorView, replaceText } from '../test/editor';
import { automationSummary, automationView, folderView, page, twoJobs } from '../test/fixtures';
import { fakeServer, info, json, me, noContent, problem, requests, type Handler } from '../test/fakeServer';
import { renderApp } from '../test/renderApp';

type Repository = components['schemas']['RepositoryView'];

const team = folderView('team', 'Team');
const commit = '0123456789abcdef0123456789abcdef01234567';

function repository(overrides: Partial<Repository> = {}): Repository {
  return {
    id: 'r1',
    folderId: 'team',
    name: 'shop',
    kind: 'GenericGit',
    url: 'https://git.example.org/team/shop.git',
    defaultBranch: null,
    branch: 'main',
    userName: null,
    hasCredential: false,
    lastSyncedAt: '2026-11-04T10:00:00Z',
    lastCommit: commit,
    lastError: null,
    pollMinutes: 5,
    nextPollAt: null,
    webhook: null,
    problems: [],
    automations: 2,
    createdAt: '2026-11-04T09:00:00Z',
    createdByName: 'ada',
    webUrl: null,
    ...overrides,
  };
}

const source = {
  repositoryId: 'r1',
  repositoryName: 'shop',
  path: '.autocanvas/automations/build.yml',
  commit,
  kind: 'GitHub',
  proposesChanges: true,
  webUrl: 'https://github.com/acme/shop',
  fileUrl: `https://github.com/acme/shop/blob/${commit}/.autocanvas/automations/build.yml`,
  commitUrl: `https://github.com/acme/shop/commit/${commit}`,
};

function server(extra: Record<string, Handler> = {}) {
  fakeServer({
    'GET /api/v1/info': () => json(info),
    'GET /api/v1/me': () => json(me()),
    'GET /api/v1/folders': () => json([team]),
    'GET /api/v1/folders/team': () => json(team, 200, { ETag: '"3"' }),
    'GET /api/v1/folders/team/children': () => json(page([])),
    'GET /api/v1/folders/team/automations': () => json(page([])),
    ...extra,
  });
}

describe('repositories in a folder', () => {
  it('show their state and problems, and sync, change and disconnect', async () => {
    let synced = 0;
    server({
      'GET /api/v1/folders/team/repositories': () =>
        json([
          repository({
            lastError: 'git: could not read from the repository',
            problems: [{ path: '.autocanvas/automations/broken.yml', message: 'jobs is empty', line: 2 }],
          }),
        ]),
      'POST /api/v1/repositories/r1/sync': () => {
        synced += 1;
        return json(repository());
      },
      'PATCH /api/v1/repositories/r1': () =>
        problem(400, 'Poll every 1 to 1440 minutes, or 0 not to poll.', 'pollMinutes'),
      'DELETE /api/v1/repositories/r1': () => noContent(),
    });
    const { user } = renderApp('/folders/team/repositories');

    const card = (await screen.findByRole('list', { name: 'Repositories' })).querySelector('li') as HTMLElement;
    expect(within(card).getByRole('heading', { name: 'shop' })).toBeInTheDocument();
    expect(within(card).getByText('Git')).toBeInTheDocument();
    expect(within(card).getByText('2 automations')).toBeInTheDocument();
    expect(within(card).getByText(commit.slice(0, 7))).toBeInTheDocument();
    expect(within(card).getByText(/polled every 5 min/)).toBeInTheDocument();
    expect(within(card).getByText('git: could not read from the repository')).toBeInTheDocument();
    await user.click(within(card).getByText('1 file is not an automation'));
    expect(within(card).getByText('.autocanvas/automations/broken.yml:2')).toBeInTheDocument();

    await user.click(within(card).getByRole('button', { name: 'Sync' }));
    await waitFor(() => expect(synced).toBe(1));

    await user.click(within(card).getByRole('button', { name: 'Actions for shop' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Change' }));
    const change = await screen.findByRole('dialog', { name: 'Change shop' });
    await user.clear(within(change).getByLabelText('Poll every (minutes)'));
    await user.type(within(change).getByLabelText('Poll every (minutes)'), '5000');
    await user.click(within(change).getByRole('button', { name: 'Save' }));
    expect(await within(change).findByText('Poll every 1 to 1440 minutes, or 0 not to poll.')).toBeInTheDocument();
    await user.keyboard('{Escape}');

    await user.click(within(card).getByRole('button', { name: 'Actions for shop' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Disconnect' }));
    const confirm = await screen.findByRole('dialog', { name: 'Disconnect shop?' });
    expect(within(confirm).getByText(/Its 2 automations are deleted from this folder/)).toBeInTheDocument();
    await user.click(within(confirm).getByRole('button', { name: 'Disconnect' }));
    await waitFor(() => expect(requests.some((request) => request.method === 'DELETE')).toBe(true));
  });

  it('make a webhook address whose secret is shown once', async () => {
    server({
      'GET /api/v1/folders/team/repositories': () => json([repository({ pollMinutes: 0 })]),
      'POST /api/v1/repositories/r1/webhook': () =>
        json(
          {
            path: '/api/v1/hooks/acg_x',
            token: 'acg_x',
            secret: 'the-secret',
            hint: 'acg_…x',
            url: 'https://ac.example.org/api/v1/hooks/acg_x',
          },
          201,
        ),
    });
    const { user } = renderApp('/folders/team/repositories');

    expect(await screen.findByText(/no pushes arrive/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Actions for shop' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Create webhook address' }));

    const dialog = await screen.findByRole('dialog', { name: 'Webhook address' });
    expect(within(dialog).getByText('https://ac.example.org/api/v1/hooks/acg_x')).toBeInTheDocument();
    expect(within(dialog).getByText('the-secret')).toBeInTheDocument();
    expect(within(dialog).getByText('GitLab')).toBeInTheDocument();
    await user.click(within(dialog).getByLabelText('I have entered them'));
    await user.click(within(dialog).getByRole('button', { name: 'Done' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('connect one from the GitHub App or by its address', async () => {
    let connected: unknown = null;
    server({
      'GET /api/v1/folders/team/repositories': () => json([]),
      'GET /api/v1/folders/team/github/repositories': () =>
        json([
          { installationId: 7, account: 'acme', id: 11, fullName: 'acme/shop', defaultBranch: 'main', private: true },
          { installationId: 7, account: 'acme', id: 12, fullName: 'acme/docs', defaultBranch: 'main', private: false },
        ]),
      'POST /api/v1/folders/team/repositories/github': async (request) => {
        connected = await request.json();
        return json(repository({ kind: 'GitHub' }), 201);
      },
      'POST /api/v1/folders/team/repositories': () =>
        problem(400, 'The repository cannot be read: git: authentication failed', 'address'),
    });
    const { user } = renderApp('/folders/team/repositories');

    expect(await screen.findByText('No repositories')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Connect a repository' }));
    const dialog = await screen.findByRole('dialog', { name: 'Connect a repository' });
    const list = await within(dialog).findByRole('list', { name: 'Repositories on GitHub' });
    await user.type(within(dialog).getByPlaceholderText('Find a repository'), 'shop');
    expect(within(list).queryByText('acme/docs')).not.toBeInTheDocument();
    await user.click(within(list).getByRole('button', { name: 'Connect' }));
    await waitFor(() => expect(connected).toEqual({ installationId: 7, repository: 'acme/shop' }));

    await user.click(screen.getByRole('button', { name: 'Connect a repository' }));
    const again = await screen.findByRole('dialog', { name: 'Connect a repository' });
    await user.click(within(again).getByRole('button', { name: 'Git address' }));
    await user.type(within(again).getByLabelText('Address'), 'https://git.example.org/team/private.git');
    await user.type(within(again).getByLabelText('Token or password'), 'wrong');
    await user.click(within(again).getByRole('button', { name: 'Connect' }));
    expect(
      await within(again).findByText('The repository cannot be read: git: authentication failed'),
    ).toBeInTheDocument();
    const sent = requests.find(
      (request) => request.url.endsWith('/folders/team/repositories') && request.method === 'POST',
    );
    expect(await sent?.json()).toEqual({
      address: 'https://git.example.org/team/private.git',
      name: null,
      defaultBranch: null,
      userName: null,
      secret: 'wrong',
      pollMinutes: 5,
    });
  });

  it('point to the GitHub App when GitHub cannot be asked', async () => {
    server({
      'GET /api/v1/folders/team/repositories': () => json([]),
      'GET /api/v1/folders/team/github/repositories': () => problem(409, 'No GitHub App is set up.'),
    });
    const { user } = renderApp('/folders/team/repositories');

    await user.click(await screen.findByRole('button', { name: 'Connect a repository' }));

    expect(await screen.findByText('No GitHub App is set up.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Administration, Forges' })).toHaveAttribute('href', '/admin/forges');
  });
});

/** The automation `a1`, from the repository shop (GitHub unless told otherwise), with what it shows. */
function withAutomation(extra: Record<string, Handler> = {}, proposes = true) {
  server({
    'GET /api/v1/folders/team/automations': () =>
      json(page([{ ...automationSummary('a1', 'build', 'Build'), repository: source }])),
    'GET /api/v1/automations/a1': () =>
      json(
        {
          ...automationView('a1', 'team'),
          repository: { ...source, proposesChanges: proposes, kind: proposes ? 'GitHub' : 'GenericGit' },
        },
        200,
        {
          ETag: '"2"',
        },
      ),
    'GET /api/v1/automations/a1/triggers': () => json({ manual: true, webhook: false, schedules: [], follows: null }),
    'GET /api/v1/automations/a1/runs': () => json(page([])),
    'GET /api/v1/node-types': () => json([]),
    'POST /api/v1/automations/validate': () => json({ valid: true, diagnostics: [] }),
    ...extra,
  });
}

describe('repository automations', () => {
  beforeAll(async () => {
    await import('./automation/EditorTab');
  }, 30_000);

  it('show where they come from and offer no rename, move or delete', async () => {
    withAutomation();
    renderApp('/automations/a1');

    const card = await screen.findByRole('region', { name: 'From repository' });
    expect(within(card).getByRole('link', { name: '.autocanvas/automations/build.yml' })).toHaveAttribute(
      'href',
      source.fileUrl,
    );
    expect(within(card).getByRole('link', { name: commit.slice(0, 7) })).toHaveAttribute('href', source.commitUrl);
    expect(screen.getByText('From shop')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Automation actions' })).not.toBeInTheDocument();
  });

  it('are proposed as a pull request from the editor', async () => {
    withAutomation({
      'POST /api/v1/automations/a1/pull-request': () =>
        json({ number: 41, url: 'https://github.com/acme/shop/pull/41', branch: 'autocanvas/build-1' }, 201),
    });
    const { user } = renderApp('/automations/a1/edit');
    const view = await editorView('File of Build');

    act(() => replaceText(view, twoJobs.replace('run: make', 'run: make -j8')));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Propose' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Propose' }));
    const dialog = await screen.findByRole('dialog', { name: 'Propose as a pull request' });
    expect(within(dialog).getByText(/The automation lives in shop/)).toBeInTheDocument();
    await user.type(within(dialog).getByRole('textbox', { name: 'Title' }), 'Faster build');
    await user.click(within(dialog).getByRole('button', { name: 'Open pull request' }));

    expect(await screen.findByRole('link', { name: 'Proposed as pull request #41' })).toHaveAttribute(
      'href',
      'https://github.com/acme/shop/pull/41',
    );
    const sent = requests.find((request) => request.url.endsWith('/pull-request'));
    expect(await sent?.json()).toEqual({
      yaml: twoJobs.replace('run: make', 'run: make -j8'),
      title: 'Faster build',
      message: null,
    });
    expect(view.state.doc.toString()).toBe(twoJobs);
  });

  it('from Generic Git are read only in the editor', async () => {
    withAutomation({}, false);
    renderApp('/automations/a1/edit');

    expect(
      await screen.findByText(/it comes from the repository shop; change .autocanvas\/automations\/build.yml there/),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument();
  });

  it('carry a badge in their folder', async () => {
    withAutomation();
    renderApp('/folders/team');

    expect(await screen.findByText('From shop')).toBeInTheDocument();
  });
});
