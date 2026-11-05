import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { beforeAll, describe, expect, it } from 'vitest';
import { editorView, replaceText } from '../../test/editor';
import { automationView, folderView, page, twoJobs } from '../../test/fixtures';
import { fakeServer, info, json, me, problem, requests, type Handler } from '../../test/fakeServer';
import { renderApp } from '../../test/renderApp';

type User = ReturnType<typeof renderApp>['user'];

/** Saves through the bar's Save button and the dialog it opens, with a message if one is given. */
async function saveVersion(user: User, message = '') {
  await waitFor(() => expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled());
  await user.click(screen.getByRole('button', { name: 'Save' }));
  const dialog = await screen.findByRole('dialog', { name: /^Save version/ });
  if (message !== '') {
    await user.type(within(dialog).getByRole('textbox', { name: 'Message' }), message);
  }
  await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Save version' })).toBeEnabled());
  await user.click(within(dialog).getByRole('button', { name: 'Save version' }));
}

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
  // The editor page is loaded on demand (CodeMirror, the canvas). Loaded once here, so the first test
  // does not spend its time limit on the import when the whole suite runs in parallel.
  beforeAll(async () => {
    await import('./EditorTab');
  }, 30_000);

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
    await user.click(screen.getByRole('button', { name: 'Save' }));
    const dialog = await screen.findByRole('dialog', { name: 'Save version 3' });
    expect(within(dialog).getByText('Fix the problems below to save.')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Save version' })).toBeDisabled();
    await user.keyboard('{Escape}');
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
    await saveVersion(user, 'Run the tests');

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
    await saveVersion(user);

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
    await saveVersion(user);

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

    await saveVersion(user);
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

    await user.click(screen.getByRole('link', { name: 'Back to Build' }));
    expect(await screen.findByRole('dialog', { name: 'Leave without saving?' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(globalThis.location.pathname).toBe('/automations/a1/edit');

    await user.click(screen.getByRole('link', { name: 'Back to Build' }));
    await user.click(await screen.findByRole('button', { name: 'Leave and lose changes' }));
    await waitFor(() => expect(globalThis.location.pathname).toBe('/automations/a1'));
  });

  it('asks for a message on Ctrl+S, from the canvas too, and saves nothing on Esc', async () => {
    server();
    const { user } = renderApp('/automations/a1/edit');
    const view = await editorView(label);
    expect(screen.queryByLabelText('Folders')).not.toBeInTheDocument();

    await user.keyboard('{Control>}s{/Control}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    act(() => replaceText(view, changed));
    screen.getByRole('button', { name: 'Tidy up' }).focus();
    await user.keyboard('{Control>}s{/Control}');
    const dialog = await screen.findByRole('dialog', { name: 'Save version 3' });
    expect(within(dialog).getByText('1 line added, 1 line removed.')).toBeInTheDocument();
    expect(within(dialog).getByRole('textbox', { name: 'Message' })).toHaveFocus();

    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(requests.some((request) => request.method === 'PUT')).toBe(false);
    expect(screen.getByText('Unsaved changes')).toBeInTheDocument();
  });

  it('searches the file on Ctrl+F also while the focus is outside it, but not in the canvas view', async () => {
    server();
    const { user } = renderApp('/automations/a1/edit');
    const view = await editorView(label);

    screen.getByRole('button', { name: 'Tidy up' }).focus();
    await user.keyboard('{Control>}f{/Control}');
    expect(view.dom.querySelector('.cm-search')).not.toBeNull();
    // CodeMirror focuses the field itself; e2e/03 checks that in a browser.
    expect(screen.getByRole('textbox', { name: 'Find' })).toBeInTheDocument();

    await user.keyboard('{Escape}');
    await user.click(screen.getByRole('button', { name: 'Canvas' }));
    screen.getByRole('button', { name: 'Tidy up' }).focus();
    const event = new KeyboardEvent('keydown', { key: 'f', ctrlKey: true, bubbles: true, cancelable: true });
    document.activeElement?.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(false);
  });

  it('only shows the file to viewers', async () => {
    server({ 'GET /api/v1/automations/a1': () => json(automationView('a1', 'team', { role: 'Viewer' })) });
    renderApp('/automations/a1/edit');
    const view = await editorView(label);

    expect(view.state.readOnly).toBe(true);
    expect(screen.getByText('Read only: changing it needs the Editor role on its folder.')).toBeInTheDocument();
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

  it('opens the side panel for a chosen card, whose typing undo takes back as one step', async () => {
    server();
    const { user } = renderApp('/automations/a1/edit');
    const view = await editorView(label);
    const canvas = screen.getByRole('region', { name: 'Canvas' });
    const card = canvas.querySelector<HTMLElement>('.react-flow__node[data-id="build"]');
    if (!card) {
      throw new Error('No card.');
    }

    // Enter on a card opens its panel and gives it the focus. (Not focused first: React Flow pans
    // to a focused card, which needs sizes that jsdom does not measure.)
    fireEvent.keyDown(card, { key: 'Enter' });
    const panel = await screen.findByRole('complementary', { name: 'Settings of build' });
    expect(within(panel).getByRole('heading', { name: 'build' })).toHaveFocus();

    await user.click(within(panel).getByRole('button', { name: /^Runs on/ }));
    await user.type(within(panel).getByLabelText('Image'), 'node:24');
    expect(view.state.doc.toString()).toContain('  build:\n    image: node:24\n    steps:\n');
    // The value typed shows in the file at once, and one undo takes all of it back.
    await user.click(within(canvas).getByRole('button', { name: 'Undo (Ctrl+Z)' }));
    expect(view.state.doc.toString()).toBe(twoJobs);

    await user.click(within(panel).getByRole('button', { name: 'Close the panel (Esc)' }));
    expect(screen.queryByRole('complementary')).not.toBeInTheDocument();
  });

  it('offers no palette to viewers', async () => {
    server({ 'GET /api/v1/automations/a1': () => json(automationView('a1', 'team', { role: 'Viewer' })) });
    renderApp('/automations/a1/edit');
    await editorView(label);

    expect(screen.queryByRole('region', { name: 'Add to the canvas' })).not.toBeInTheDocument();
    expect(screen.getByText('You can read the canvas; changing it needs the Editor role.')).toBeInTheDocument();
  });
});
