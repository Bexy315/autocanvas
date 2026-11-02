import { act, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { editorView, replaceText } from '../../test/editor';
import { automationView, folderView, page, twoJobs } from '../../test/fixtures';
import { fakeServer, info, json, me, problem, requests, type Handler } from '../../test/fakeServer';
import { renderApp } from '../../test/renderApp';

const team = folderView('team', 'Team');
const label = 'File of Build';
const changed = twoJobs.replace('run: make', 'run: make test');

/** The automation `a1` (version 2) in Team, a validator that finds `tset`, and whatever the test adds. */
function server(extra: Record<string, Handler> = {}) {
  fakeServer({
    'GET /api/v1/info': () => json(info),
    'GET /api/v1/me': () => json(me()),
    'GET /api/v1/folders': () => json([team]),
    'GET /api/v1/folders/team': () => json(team),
    'GET /api/v1/folders/team/children': () => json(page([])),
    'GET /api/v1/node-types': () => json([]),
    'GET /api/v1/automations/a1': () => json(automationView('a1', 'team'), 200, { ETag: '"2"' }),
    'POST /api/v1/automations/validate': async (request) => {
      const body = (await request.json()) as { yaml: string };
      return json(
        body.yaml.includes('tset')
          ? {
              valid: false,
              diagnostics: [
                {
                  severity: 'Error',
                  code: 'AC0201',
                  message: "There is no job 'tset'.",
                  path: 'jobs.build.needs[0]',
                  line: 6,
                  column: 13,
                },
              ],
            }
          : { valid: true, diagnostics: [] },
      );
    },
    ...extra,
  });
}

const validations = () => requests.filter((request) => request.url.endsWith('/validate'));

describe('the editor', () => {
  it('checks the file while typing and points at the problems', async () => {
    server();
    const { user } = renderApp('/automations/a1/edit');
    const view = await editorView(label);
    expect(view.state.doc.toString()).toBe(twoJobs);
    expect(await screen.findByText('No problems found.')).toBeInTheDocument();

    act(() => replaceText(view, `${twoJobs}    needs: [tset]\n`));

    expect(screen.getByText('Unsaved changes')).toBeInTheDocument();
    const problems = await screen.findByRole('list', { name: 'Problems in the file' });
    expect(problems).toHaveTextContent("There is no job 'tset'.");
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
    const last = validations().at(-1);
    expect(await last?.json()).toEqual({ yaml: `${twoJobs}    needs: [tset]\n`, folderId: 'team', automationId: 'a1' });

    await user.click(within(problems).getByRole('button', { name: /There is no job/ }));
    const line = view.state.doc.lineAt(view.state.selection.main.head);
    expect(line.number).toBe(6);
  });

  it('saves a new version with a message and the version it started from', async () => {
    server({ 'PUT /api/v1/automations/a1': () => json(automationView('a1', 'team', { version: 3, yaml: changed })) });
    const { user } = renderApp('/automations/a1/edit');
    const view = await editorView(label);

    act(() => replaceText(view, changed));
    await user.type(screen.getByRole('textbox', { name: 'Message' }), 'Run the tests');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Saved version 3')).toBeInTheDocument();
    const put = requests.find((request) => request.method === 'PUT');
    expect(put?.headers.get('If-Match')).toBe('"2"');
    expect(await put?.json()).toEqual({ yaml: changed, message: 'Run the tests' });
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
  });

  it('shows what the server refused on saving', async () => {
    server({
      'PUT /api/v1/automations/a1': () =>
        problem(400, 'The automation file has a problem; see diagnostics.', 'yaml', {
          diagnostics: [
            {
              severity: 'Error',
              code: 'AC0420',
              message: 'You may not see deploy.',
              path: 'triggers',
              line: 3,
              column: 3,
            },
          ],
        }),
    });
    const { user } = renderApp('/automations/a1/edit');
    const view = await editorView(label);

    act(() => replaceText(view, changed));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('list', { name: 'Problems in the file' })).toHaveTextContent(
      'You may not see deploy.',
    );
  });

  it('compares with a version saved in between, then saves on top of it', async () => {
    let saves = 0;
    server({
      'PUT /api/v1/automations/a1': () => {
        saves += 1;
        return saves === 1
          ? problem(412, 'The automation changed.')
          : json(automationView('a1', 'team', { version: 4, yaml: changed }));
      },
    });
    const { user } = renderApp('/automations/a1/edit');
    const view = await editorView(label);
    act(() => replaceText(view, changed));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled());

    server({
      'GET /api/v1/automations/a1': () =>
        json(automationView('a1', 'team', { version: 3, yaml: twoJobs.replace('Build', 'Build it') }), 200, {
          ETag: '"3"',
        }),
      'PUT /api/v1/automations/a1': () => {
        saves += 1;
        return saves === 1
          ? problem(412, 'The automation changed.')
          : json(automationView('a1', 'team', { version: 4, yaml: changed }));
      },
    });
    await user.click(screen.getByRole('button', { name: 'Save' }));

    const dialog = await screen.findByRole('dialog', { name: 'Someone saved in the meantime' });
    const diff = await within(dialog).findByRole('region', { name: 'Your changes compared with version 3' });
    expect(diff).toHaveTextContent('make test');
    await user.click(within(dialog).getByRole('button', { name: 'Save mine on top' }));

    expect(await screen.findByText('Saved version 4')).toBeInTheDocument();
    const puts = requests.filter((request) => request.method === 'PUT');
    expect(puts.map((request) => request.headers.get('If-Match'))).toEqual(['"2"', '"3"']);
  });

  it('takes the version saved in between instead, when asked', async () => {
    const theirs = twoJobs.replace('Build', 'Build it');
    server({ 'PUT /api/v1/automations/a1': () => problem(412, 'The automation changed.') });
    const { user } = renderApp('/automations/a1/edit');
    const view = await editorView(label);
    act(() => replaceText(view, changed));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled());
    server({
      'GET /api/v1/automations/a1': () =>
        json(automationView('a1', 'team', { version: 3, yaml: theirs }), 200, { ETag: '"3"' }),
      'PUT /api/v1/automations/a1': () => problem(412, 'The automation changed.'),
    });

    await user.click(screen.getByRole('button', { name: 'Save' }));
    await user.click(await screen.findByRole('button', { name: 'Load theirs' }));

    await waitFor(() => expect(view.state.doc.toString()).toBe(theirs));
    expect(screen.getByText('No changes')).toBeInTheDocument();
  });

  it('discards changes after asking', async () => {
    server();
    const { user } = renderApp('/automations/a1/edit');
    const view = await editorView(label);
    act(() => replaceText(view, changed));

    await user.click(screen.getByRole('button', { name: 'Discard' }));
    await user.click(screen.getByRole('button', { name: 'Discard changes' }));

    expect(view.state.doc.toString()).toBe(twoJobs);
    expect(screen.getByRole('button', { name: 'Discard' })).toBeDisabled();
  });

  it('asks before leaving with unsaved changes', async () => {
    server({
      'GET /api/v1/automations/a1/triggers': () => json({ manual: true, webhook: false, schedules: [], follows: null }),
      'GET /api/v1/automations/a1/runs': () => json(page([])),
    });
    const { user } = renderApp('/automations/a1/edit');
    const view = await editorView(label);
    act(() => replaceText(view, changed));

    await user.click(screen.getByRole('link', { name: 'Overview' }));
    expect(await screen.findByRole('dialog', { name: 'Leave without saving?' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(globalThis.location.pathname).toBe('/automations/a1/edit');

    await user.click(screen.getByRole('link', { name: 'Overview' }));
    await user.click(await screen.findByRole('button', { name: 'Leave and lose changes' }));
    await waitFor(() => expect(globalThis.location.pathname).toBe('/automations/a1'));
  });

  it('only shows the file to viewers', async () => {
    server({ 'GET /api/v1/automations/a1': () => json(automationView('a1', 'team', { role: 'Viewer' })) });
    renderApp('/automations/a1/edit');
    const view = await editorView(label);

    expect(view.state.readOnly).toBe(true);
    expect(screen.getByText(/Changing it needs the Editor role/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument();
    expect(validations()).toHaveLength(0);
  });

  it('shows the canvas next to the file and follows the cursor both ways', async () => {
    server();
    const { user } = renderApp('/automations/a1/edit');
    const view = await editorView(label);
    const canvas = screen.getByRole('region', { name: 'Canvas' });

    await user.click(within(canvas).getByRole('button', { name: 'List' }));
    const jobs = await within(canvas).findByRole('list', { name: 'Jobs' });
    await user.click(within(jobs).getByRole('button', { name: /^build/ }));
    expect(view.state.doc.lineAt(view.state.selection.main.head).text).toBe('  build:');

    act(() => view.dispatch({ selection: { anchor: view.state.doc.line(1).from } }));
    await waitFor(() =>
      expect(within(jobs).getByRole('button', { name: /^build/ })).not.toHaveAttribute('aria-current'),
    );
    act(() => view.dispatch({ selection: { anchor: view.state.doc.line(8).from } }));
    await waitFor(() =>
      expect(within(jobs).getByRole('button', { name: /^build/ })).toHaveAttribute('aria-current', 'true'),
    );
  });

  it('follows the file while typing and keeps the last readable state', async () => {
    server();
    renderApp('/automations/a1/edit');
    const view = await editorView(label);
    const canvas = screen.getByRole('region', { name: 'Canvas' });

    act(() =>
      replaceText(
        view,
        `${twoJobs}  test:
    needs: [build]
    steps: [{ run: make test }]
`,
      ),
    );
    expect(await within(canvas).findByLabelText('Job test, 1 step, needs build')).toBeInTheDocument();

    act(() =>
      replaceText(
        view,
        `${twoJobs}  broken: [
`,
      ),
    );
    expect(await within(canvas).findByText(/fix it in the YAML to edit on the canvas again/)).toBeInTheDocument();
    expect(within(canvas).getByLabelText('Job test, 1 step, needs build')).toBeInTheDocument();
  });

  it('shows the canvas, the file or both, and remembers the choice', async () => {
    server();
    const { user } = renderApp('/automations/a1/edit');
    await editorView(label);
    const views = screen.getByRole('group', { name: 'Show' });

    await user.click(within(views).getByRole('button', { name: 'YAML' }));
    expect(screen.getByRole('region', { name: 'Canvas', hidden: true })).toHaveClass('hidden');
    expect(localStorage.getItem('autocanvas.editor.view')).toBe('yaml');
    await user.click(within(views).getByRole('button', { name: 'Canvas' }));
    expect(screen.getByRole('region', { name: 'File', hidden: true })).toHaveClass('hidden');
    await user.click(within(views).getByRole('button', { name: 'Split' }));
    expect(screen.getByRole('separator')).toBeInTheDocument();
  });

  it('adds a job from the palette into the file, as one step undo takes back', async () => {
    server();
    const { user } = renderApp('/automations/a1/edit');
    const view = await editorView(label);
    const canvas = screen.getByRole('region', { name: 'Canvas' });

    await user.click(within(canvas).getByRole('button', { name: /^Add Job:/ }));

    const text = view.state.doc.toString();
    expect(text).toContain('  job:\n    runs_on: linux\n');
    expect(text).toContain('x-editor:\n  layout:\n');
    expect(text.startsWith(twoJobs)).toBe(true);
    expect(await within(canvas).findByText('Added job job')).toBeInTheDocument();
    expect(screen.getByText('Unsaved changes')).toBeInTheDocument();

    await user.click(within(canvas).getByRole('button', { name: 'Undo (Ctrl+Z)' }));
    expect(view.state.doc.toString()).toBe(twoJobs);
    await user.click(within(canvas).getByRole('button', { name: 'Redo (Ctrl+Y)' }));
    expect(view.state.doc.toString()).toBe(text);

    await user.click(within(canvas).getByRole('button', { name: 'Tidy up' }));
    expect(view.state.doc.toString()).toContain('    trigger: {x:');
  });

  it('offers no palette to viewers', async () => {
    server({ 'GET /api/v1/automations/a1': () => json(automationView('a1', 'team', { role: 'Viewer' })) });
    renderApp('/automations/a1/edit');
    await editorView(label);

    expect(screen.queryByRole('toolbar', { name: 'Add to the canvas' })).not.toBeInTheDocument();
    expect(screen.getByText('You can read the canvas; changing it needs the Editor role.')).toBeInTheDocument();
  });
});
