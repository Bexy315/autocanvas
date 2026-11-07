import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { automationView, folderSummary, folderView, page, twoJobs } from '../../test/fixtures';
import { fakeServer, info, json, me, noContent, problem, requests, type Handler } from '../../test/fakeServer';
import { renderApp } from '../../test/renderApp';

const team = folderView('team', 'Team');
const nextAt = new Date(Date.now() + 5 * 3600_000).toISOString();
const version1 = 'version: 1\nname: Build\njobs:\n  build:\n    steps:\n      - run: make\n';

function versionView(number: number, yaml: string, message: string | null) {
  return { number, message, createdAt: '2026-11-01T10:00:00Z', createdById: null, createdByName: 'ada', yaml };
}

/** An automation `a1` in the Team folder (version 2), and whatever the test adds. */
function server(extra: Record<string, Handler> = {}) {
  fakeServer({
    'GET /api/v1/info': () => json(info),
    'GET /api/v1/me': () => json(me()),
    'GET /api/v1/folders': () => json([team]),
    'GET /api/v1/folders/team': () => json(team, 200, { ETag: '"3"' }),
    'GET /api/v1/folders/team/children': () => json(page([])),
    'GET /api/v1/folders/team/automations': () => json(page([])),
    'GET /api/v1/automations/a1': () => json(automationView('a1', 'team'), 200, { ETag: '"2"' }),
    'GET /api/v1/automations/a1/triggers': () =>
      json({
        manual: true,
        webhook: true,
        schedules: [
          {
            position: 0,
            schedule: '0 3 * * *',
            timeZone: 'Europe/Berlin',
            nextAt,
            lastAt: null,
          },
        ],
        follows: { folderId: 'team', automation: 'tests', on: 'Succeeded' },
      }),
    'GET /api/v1/automations/a1/runs': () =>
      json(
        page([
          {
            id: 'r2',
            number: 2,
            automationId: 'a1',
            automationName: 'build',
            state: 'Failed',
            trigger: 'Schedule',
            triggeredByName: 'schedule',
            createdAt: new Date().toISOString(),
            finishedAt: null,
          },
          {
            id: 'r1',
            number: 1,
            automationId: 'a1',
            automationName: 'build',
            state: 'Succeeded',
            trigger: 'Manual',
            triggeredByName: 'ada',
            createdAt: '2026-11-01T10:00:00Z',
            finishedAt: null,
          },
        ]),
      ),
    'GET /api/v1/automations/a1/versions': () =>
      json(
        page([
          {
            number: 2,
            message: 'Add triggers',
            createdAt: '2026-11-01T12:00:00Z',
            createdById: null,
            createdByName: 'ada',
          },
          { number: 1, message: null, createdAt: '2026-11-01T10:00:00Z', createdById: null, createdByName: 'ada' },
        ]),
      ),
    'GET /api/v1/automations/a1/versions/1': () => json(versionView(1, version1, null)),
    'GET /api/v1/automations/a1/versions/2': () => json(versionView(2, twoJobs, 'Add triggers')),
    ...extra,
  });
}

describe("an automation's overview", () => {
  it('shows what starts it, its latest runs, its warnings and its file', async () => {
    server({
      'GET /api/v1/automations/a1': () =>
        json(
          automationView('a1', 'team', {
            warnings: [
              {
                severity: 'Warning',
                code: 'AC0301',
                message: 'The job has no runs_on.',
                path: 'jobs.build',
                line: 6,
                column: 3,
              },
            ],
          }),
        ),
    });
    const { user } = renderApp('/automations/a1');

    expect(await screen.findByRole('heading', { name: 'Build', level: 1 })).toBeInTheDocument();
    expect(screen.getByText('Builds the shop.')).toBeInTheDocument();
    expect(await screen.findByText('Schedule: 0 3 * * * (Europe/Berlin)')).toBeInTheDocument();
    const next = screen.getByText('in 5 hours');
    expect(next).toHaveAttribute('datetime', nextAt);
    expect(next.closest('p')).toHaveTextContent('Next in 5 hours');
    expect(screen.getByText('After tests')).toBeInTheDocument();
    expect(screen.getByText('Webhook')).toBeInTheDocument();
    expect(await screen.findByText('Failed')).toBeInTheDocument();
    expect(screen.getByText('#1')).toBeInTheDocument();
    expect(screen.getByRole('list', { name: 'Warnings' })).toHaveTextContent(
      'The job has no runs_on.Line 6:3 · AC0301 · jobs.build',
    );
    // The jobs as a canvas first; the file is one switch away, and the choice is remembered.
    expect(screen.getByRole('region', { name: 'Canvas of Build' })).toHaveTextContent('make');
    await user.click(screen.getByRole('button', { name: 'File' }));
    expect(screen.getByRole('region', { name: 'File of Build' })).toHaveTextContent('- run: make');
    expect(localStorage.getItem('autocanvas.overview.view')).toBe('file');
    await user.click(screen.getByRole('button', { name: 'Automation actions' }));
    expect(screen.getByRole('menuitem', { name: 'Download' })).toHaveAttribute('href', '/api/v1/automations/a1/yaml');
    await user.keyboard('{Escape}');
    const crumbs = screen.getByRole('navigation', { name: 'Breadcrumbs' });
    expect(within(crumbs).getByRole('link', { name: 'Team' })).toHaveAttribute('href', '/folders/team');
  });

  it('says when nothing starts it and it never ran', async () => {
    server({
      'GET /api/v1/automations/a1/triggers': () =>
        json({ manual: false, webhook: false, schedules: [], follows: null }),
      'GET /api/v1/automations/a1/runs': () => json(page([])),
    });
    renderApp('/automations/a1');

    expect(await screen.findByText('Nothing starts it yet. Add a trigger to the file.')).toBeInTheDocument();
    expect(await screen.findByText('It has not run yet.')).toBeInTheDocument();
  });

  it('explains when it cannot be loaded', async () => {
    server({ 'GET /api/v1/automations/a1': () => problem(404, 'There is no such automation.') });
    renderApp('/automations/a1');

    expect(await screen.findByRole('alert')).toHaveTextContent('There is no such automation.');
  });

  it('offers no changes to viewers', async () => {
    server({ 'GET /api/v1/automations/a1': () => json(automationView('a1', 'team', { role: 'Viewer' })) });
    const { user } = renderApp('/automations/a1');

    expect(await screen.findByRole('heading', { name: 'Build', level: 1 })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Run' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Automation actions' }));
    expect(screen.getAllByRole('menuitem').map((item) => item.textContent)).toEqual(['Download']);
  });
});

describe("an automation's versions", () => {
  it('shows what changed in the selected one, or its whole file', async () => {
    server();
    const { user } = renderApp('/automations/a1/versions');

    const list = await screen.findByRole('list', { name: 'Versions' });
    expect(await within(list).findByRole('link', { name: /Version 2/ })).toHaveAttribute('aria-current', 'page');
    const diff = await screen.findByRole('region', { name: 'Changes from version 1' });
    expect(within(diff).getByText('triggers:').closest('tr')).toHaveClass('bg-ok-soft');
    expect(screen.queryByRole('button', { name: 'Restore' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Full file' }));
    expect(screen.getByRole('region', { name: 'File of version 2' })).toHaveTextContent('manual: true');

    await user.click(within(list).getByRole('link', { name: /Version 1/ }));
    expect(await screen.findByText(/The first version: there is nothing to compare it with/)).toBeInTheDocument();
    expect(globalThis.location.pathname).toBe('/automations/a1/versions/1');
  });

  it('collapses unchanged lines and leaves out canvas positions', async () => {
    const long = Array.from({ length: 20 }, (_, index) => `# line ${index}`).join('\n');
    server({
      'GET /api/v1/automations/a1/versions/1': () => json(versionView(1, `${long}\nx-editor:\n  layout: {}\n`, null)),
      'GET /api/v1/automations/a1/versions/2': () =>
        json(versionView(2, `${long.replace('# line 10', '# line ten')}\nx-editor:\n  layout: { a: 1 }\n`, 'Tidy')),
    });
    renderApp('/automations/a1/versions/2');

    expect(await screen.findByText('7 unchanged lines')).toBeInTheDocument();
    expect(screen.getByText('6 unchanged lines')).toBeInTheDocument();
    expect(screen.getByText('Canvas positions (x-editor) are left out of this comparison.')).toBeInTheDocument();
  });

  it('restores an old version as a new one', async () => {
    let restored = false;
    server({
      'GET /api/v1/automations/a1': () =>
        json(automationView('a1', 'team', { version: restored ? 3 : 2 }), 200, { ETag: restored ? '"3"' : '"2"' }),
      'PUT /api/v1/automations/a1': () => {
        restored = true;
        return json(automationView('a1', 'team', { version: 3 }));
      },
      'GET /api/v1/automations/a1/versions/3': () => json(versionView(3, version1, 'Restore version 1')),
    });
    const { user } = renderApp('/automations/a1/versions/1');

    await user.click(await screen.findByRole('button', { name: 'Restore' }));
    await user.click(screen.getByRole('button', { name: 'Restore version 1' }));

    await waitFor(() => expect(globalThis.location.pathname).toBe('/automations/a1/versions/3'));
    const put = requests.find((request) => request.method === 'PUT');
    expect(put?.headers.get('If-Match')).toBe('"2"');
    expect(await put?.json()).toEqual({ yaml: version1, message: 'Restore version 1' });
  });
});

describe('creating an automation', () => {
  it('starts from the starter file and opens it', async () => {
    server({
      'POST /api/v1/folders/team/automations': () => json(automationView('a1', 'team'), 201),
    });
    const { user } = renderApp('/folders/team');

    await user.click(await screen.findByRole('button', { name: 'New automation' }));
    const dialog = await screen.findByRole('dialog', { name: 'New automation in Team' });
    await user.type(within(dialog).getByLabelText('Name'), 'build');
    await user.type(within(dialog).getByLabelText('Message'), 'First try');
    await user.click(within(dialog).getByRole('button', { name: 'Create automation' }));

    expect(await screen.findByRole('heading', { name: 'Build', level: 1 })).toBeInTheDocument();
    const body = (await requests.find((request) => request.method === 'POST')?.json()) as {
      name: string;
      yaml: string;
      message: string;
    };
    expect(body.name).toBe('build');
    expect(body.message).toBe('First try');
    expect(body.yaml).toContain('name: "build"');
    expect(body.yaml).toContain('manual: true');
  });

  it('takes a file from the computer and lists its problems', async () => {
    server({
      'POST /api/v1/folders/team/automations': () =>
        problem(400, 'The automation file has a problem; see diagnostics.', 'yaml', {
          diagnostics: [
            {
              severity: 'Error',
              code: 'AC0201',
              message: "There is no job 'tset'.",
              path: 'jobs.build.needs[0]',
              line: 7,
              column: 13,
            },
            { severity: 'Error', code: 'AC0001', message: 'The file is empty.', path: '', line: null, column: null },
          ],
        }),
    });
    const { user } = renderApp('/folders/team');

    await user.click(await screen.findByRole('button', { name: 'New automation' }));
    await user.click(screen.getByLabelText(/A file from your computer/));
    await user.upload(
      screen.getByLabelText('Automation file'),
      new File(['jobs: {}'], 'nightly.yaml', { type: 'application/yaml' }),
    );
    expect(screen.getByLabelText('Name')).toHaveValue('nightly');
    await user.click(screen.getByRole('button', { name: 'Create automation' }));

    const problems = await screen.findByRole('list', { name: 'Problems in the file' });
    expect(problems).toHaveTextContent("There is no job 'tset'.Line 7:13 · AC0201 · jobs.build.needs[0]");
    expect(problems).toHaveTextContent('The file is empty.File · AC0001');
    const body = (await requests.find((request) => request.method === 'POST')?.json()) as { yaml: string };
    expect(body.yaml).toBe('jobs: {}');
  });

  it('shows a taken name at the field', async () => {
    server({ 'POST /api/v1/folders/team/automations': () => problem(409, 'The name build is taken.', 'name') });
    const { user } = renderApp('/folders/team');

    await user.click(await screen.findByRole('button', { name: 'New automation' }));
    await user.type(screen.getByLabelText('Name'), 'build');
    await user.click(screen.getByRole('button', { name: 'Create automation' }));

    await waitFor(() => expect(screen.getByLabelText('Name')).toHaveAccessibleDescription(/The name build is taken/));
  });
});

describe('changing an automation', () => {
  it('is renamed with its version', async () => {
    server({ 'PATCH /api/v1/automations/a1': () => json(automationView('a1', 'team')) });
    const { user } = renderApp('/automations/a1');

    await user.click(await screen.findByRole('button', { name: 'Automation actions' }));
    await user.click(screen.getByRole('menuitem', { name: 'Rename' }));
    const name = screen.getByLabelText('Name');
    await user.clear(name);
    await user.type(name, 'build-shop');
    await user.click(screen.getByRole('button', { name: 'Rename' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    const patch = requests.find((request) => request.method === 'PATCH');
    expect(patch?.headers.get('If-Match')).toBe('"2"');
    expect(await patch?.json()).toEqual({ name: 'build-shop' });
  });

  it('is moved to another folder', async () => {
    server({
      'GET /api/v1/folders': () => json([team, folderView('ops', 'Ops')]),
      'GET /api/v1/folders/ops/children': () => json(page([folderSummary('prod', 'Prod', 'Viewer')])),
      'POST /api/v1/automations/a1/move': () => json(automationView('a1', 'ops')),
    });
    const { user } = renderApp('/automations/a1');

    await user.click(await screen.findByRole('button', { name: 'Automation actions' }));
    await user.click(screen.getByRole('menuitem', { name: 'Move' }));
    const picker = await screen.findByRole('tree', { name: 'Target folder' });
    expect(within(picker).getByRole('treeitem', { name: 'Team' })).toHaveAttribute('aria-disabled', 'true');
    expect(screen.queryByRole('button', { name: 'Top level' })).not.toBeInTheDocument();
    await user.click(within(picker).getByRole('button', { name: 'Ops' }));
    await user.click(screen.getByRole('button', { name: 'Move here' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    const move = requests.find((request) => request.url.endsWith('/move'));
    expect(await move?.json()).toEqual({ folderId: 'ops' });
  });

  it('is deleted and leaves the user in its folder', async () => {
    server({ 'DELETE /api/v1/automations/a1': () => noContent() });
    const { user } = renderApp('/automations/a1');

    await user.click(await screen.findByRole('button', { name: 'Automation actions' }));
    await user.click(screen.getByRole('menuitem', { name: 'Delete' }));
    expect(screen.getByRole('dialog')).toHaveTextContent('all 2 versions');
    await user.click(screen.getByRole('button', { name: 'Delete automation' }));

    await waitFor(() => expect(globalThis.location.pathname).toBe('/folders/team'));
  });
});
