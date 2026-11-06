import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { automationSummary, automationView, folderSummary, folderView, page } from '../test/fixtures';
import { fakeServer, info, json, me, noContent, problem, requests, type Handler } from '../test/fakeServer';
import { renderApp } from '../test/renderApp';

const team = folderView('team', 'Team');
const backend = folderView('backend', 'Backend', { path: [{ id: 'team', name: 'Team' }] });

/** A signed-in admin, the Team folder with Backend below it, and whatever the test adds. */
function server(extra: Record<string, Handler> = {}) {
  fakeServer({
    'GET /api/v1/info': () => json(info),
    'GET /api/v1/me': () => json(me()),
    'GET /api/v1/folders': () => json([team]),
    'GET /api/v1/folders/team': () => json(team, 200, { ETag: '"3"' }),
    'GET /api/v1/folders/team/children': () => json(page([folderSummary('backend', 'Backend')])),
    'GET /api/v1/folders/team/automations': () => json(page([])),
    'GET /api/v1/folders/backend': () => json(backend, 200, { ETag: '"5"' }),
    'GET /api/v1/folders/backend/children': () => json(page([])),
    'GET /api/v1/folders/backend/automations': () => json(page([automationSummary('a1', 'build', 'Build the shop')])),
    ...extra,
  });
}

describe('the start page', () => {
  it('lists where the user can start, with the path above folders deep in a tree', async () => {
    server({
      'GET /api/v1/folders': () =>
        json([
          team,
          folderView('deep', 'Payments', {
            path: [
              { id: null, name: 'Company' },
              { id: null, name: 'Finance' },
            ],
            role: 'Viewer',
          }),
        ]),
    });
    renderApp('/');

    const list = await screen.findByRole('list', { name: 'Folders' });
    expect(within(list).getByRole('link', { name: /Team/ })).toHaveAttribute('href', '/folders/team');
    expect(within(list).getByText('In Company / Finance')).toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Breadcrumbs' })).toHaveTextContent(/^Automations$/);
  });

  it('invites to create the first folder and opens it', async () => {
    let created = false;
    server({
      'GET /api/v1/folders': () => json(created ? [team] : []),
      'POST /api/v1/folders': async (request) => {
        created = true;
        expect(await request.json()).toEqual({ name: 'Team', description: 'Our builds', parentId: null });
        return json(team, 201);
      },
    });
    const { user } = renderApp('/');

    await user.click(await screen.findByRole('button', { name: 'New folder' }));
    const dialog = await screen.findByRole('dialog', { name: 'New folder' });
    expect(within(dialog).getByLabelText('Name')).toHaveFocus();
    await user.type(within(dialog).getByLabelText('Name'), 'Team');
    await user.type(within(dialog).getByLabelText('Description'), 'Our builds');
    await user.click(within(dialog).getByRole('button', { name: 'Create folder' }));

    expect(await screen.findByRole('heading', { name: 'Team', level: 1 })).toBeInTheDocument();
    expect(globalThis.location.pathname).toBe('/folders/team');
  });

  it('shows a refused name at the field', async () => {
    server({
      'GET /api/v1/folders': () => json([]),
      'POST /api/v1/folders': () => problem(409, 'There is already a folder named Team.', 'name'),
    });
    const { user } = renderApp('/');

    await user.click(await screen.findByRole('button', { name: 'New folder' }));
    await user.type(screen.getByLabelText('Name'), 'Team');
    await user.click(screen.getByRole('button', { name: 'Create folder' }));

    await waitFor(() => expect(screen.getByLabelText('Name')).toBeInvalid());
    expect(screen.getByLabelText('Name')).toHaveAccessibleDescription(/There is already a folder named Team/);
  });
});

describe('a folder', () => {
  it('lists its subfolders and automations, with its path', async () => {
    server();
    renderApp('/folders/backend');

    expect(await screen.findByRole('link', { name: /Build the shop/ })).toHaveAttribute('href', '/automations/a1');
    const crumbs = screen.getByRole('navigation', { name: 'Breadcrumbs' });
    expect(within(crumbs).getByRole('link', { name: 'Team' })).toHaveAttribute('href', '/folders/team');
    expect(within(crumbs).getByText('Backend')).toHaveAttribute('aria-current', 'page');
    expect(screen.getByText('Version 2')).toBeInTheDocument();
    expect(screen.getByText('build')).toBeInTheDocument();
  });

  it('loads more when there are more', async () => {
    server({
      'GET /api/v1/folders/backend/automations': (request) =>
        new URL(request.url).searchParams.get('cursor') === 'p2'
          ? json(page([automationSummary('a2', 'deploy')]))
          : json(page([automationSummary('a1', 'build')], 'p2')),
    });
    const { user } = renderApp('/folders/backend');

    await user.click(await screen.findByRole('button', { name: 'Show more automations' }));

    expect(await screen.findByRole('link', { name: /deploy/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Show more automations' })).not.toBeInTheDocument();
  });

  it('offers only what the role allows', async () => {
    server({ 'GET /api/v1/folders/backend': () => json(folderView('backend', 'Backend', { role: 'Viewer' })) });
    renderApp('/folders/backend');

    expect(await screen.findByRole('heading', { name: 'Backend', level: 1 })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'New automation' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Folder actions' })).not.toBeInTheDocument();
  });

  it('says what to do when it is empty', async () => {
    server({ 'GET /api/v1/folders/team/children': () => json(page([])) });
    renderApp('/folders/team');

    expect(await screen.findByRole('heading', { name: 'This folder is empty' })).toBeInTheDocument();
    expect(screen.getByText('Create an automation or a subfolder in it.')).toBeInTheDocument();
  });

  it('is renamed with its version, and a change in between is explained', async () => {
    server({ 'PATCH /api/v1/folders/backend': () => problem(412, 'The folder changed.') });
    const { user } = renderApp('/folders/backend');

    await user.click(await screen.findByRole('button', { name: 'Folder actions' }));
    await user.click(screen.getByRole('menuitem', { name: 'Rename' }));
    const name = screen.getByLabelText('Name');
    await user.clear(name);
    await user.type(name, 'Services');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Someone changed this in the meantime');
    const patch = requests.find((request) => request.method === 'PATCH');
    expect(patch?.headers.get('If-Match')).toBe('"5"');
    expect(await patch?.json()).toEqual({ name: 'Services', description: null });
  });

  it('is moved to a folder where the user is an editor', async () => {
    server({
      'GET /api/v1/folders': () => json([team, folderView('ops', 'Ops', { role: 'Viewer' })]),
      'POST /api/v1/folders/backend/move': () => json(backend),
    });
    const { user } = renderApp('/folders/backend');

    await user.click(await screen.findByRole('button', { name: 'Folder actions' }));
    await user.click(screen.getByRole('menuitem', { name: 'Move' }));
    const picker = await screen.findByRole('tree', { name: 'Target folder' });
    const ops = await within(picker).findByRole('treeitem', { name: /Ops/ });
    expect(ops).toHaveAttribute('aria-disabled', 'true');
    await user.click(screen.getByRole('button', { name: 'Top level' }));
    expect(screen.getByText('It moves to the top level.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Move here' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    const move = requests.find((request) => request.url.endsWith('/move'));
    expect(await move?.json()).toEqual({ parentId: null });
  });

  it('is deleted when empty, and the reason is shown when it is not', async () => {
    let attempts = 0;
    server({
      'DELETE /api/v1/folders/backend': () => {
        attempts += 1;
        return attempts === 1 ? problem(409, 'The folder is not empty.') : noContent();
      },
    });
    const { user } = renderApp('/folders/backend');

    await user.click(await screen.findByRole('button', { name: 'Folder actions' }));
    await user.click(screen.getByRole('menuitem', { name: 'Delete' }));
    await user.click(screen.getByRole('button', { name: 'Delete folder' }));
    expect(await within(screen.getByRole('dialog')).findByRole('alert')).toHaveTextContent('The folder is not empty.');
    await user.click(screen.getByRole('button', { name: 'Delete folder' }));

    await waitFor(() => expect(globalThis.location.pathname).toBe('/folders/team'));
  });
});

describe('the folder tree', () => {
  it('shows the path to the current folder, and opens a folder when it is chosen', async () => {
    server({
      'GET /api/v1/folders/team/children': () =>
        json(page([folderSummary('backend', 'Backend'), folderSummary('docs', 'Docs')])),
      'GET /api/v1/folders/docs': () => json(folderView('docs', 'Docs', { path: [{ id: 'team', name: 'Team' }] })),
      'GET /api/v1/folders/docs/children': () => json(page([])),
      'GET /api/v1/folders/docs/automations': () => json(page([])),
    });
    const { user } = renderApp('/folders/backend');

    const tree = await screen.findByRole('tree', { name: 'Folders and automations' });
    expect(await within(tree).findByRole('treeitem', { name: /Backend/, selected: true })).toBeInTheDocument();
    expect(within(tree).getByRole('treeitem', { name: /Team/ })).toHaveAttribute('aria-expanded', 'true');

    await user.click(within(tree).getByRole('button', { name: /Docs/ }));

    expect(await screen.findByRole('heading', { name: 'Docs', level: 1 })).toBeInTheDocument();
    expect(await within(tree).findByRole('treeitem', { name: /Docs/, selected: true })).toBeInTheDocument();
  });

  it('is used with the keyboard', async () => {
    server({
      'GET /api/v1/folders/docs': () => json(folderView('docs', 'Docs')),
      'GET /api/v1/folders/docs/children': () => json(page([])),
      'GET /api/v1/folders/docs/automations': () => json(page([])),
    });
    const { user } = renderApp('/');

    const tree = await screen.findByRole('tree', { name: 'Folders and automations' });
    const teamItem = await within(tree).findByRole('treeitem', { name: /Team/ });
    expect(teamItem).toHaveAttribute('tabindex', '0');
    teamItem.focus();
    await user.keyboard('{ArrowRight}');
    const backendItem = await within(tree).findByRole('treeitem', { name: /Backend/ });
    expect(teamItem).toHaveAttribute('aria-expanded', 'true');
    await user.keyboard('{ArrowDown}');
    expect(backendItem).toHaveFocus();
    await user.keyboard('{ArrowLeft}');
    expect(teamItem).toHaveFocus();
    await user.keyboard('{ArrowLeft}');
    expect(teamItem).toHaveAttribute('aria-expanded', 'false');
    await user.keyboard('{End}{Home}{Enter}');

    expect(await screen.findByRole('heading', { name: 'Team', level: 1 })).toBeInTheDocument();
  });

  it('shows the automations of an open folder after its subfolders, and opens one when it is chosen', async () => {
    server({
      'GET /api/v1/automations/a1': () => json(automationView('a1', 'backend')),
      'GET /api/v1/folders/backend': () =>
        json(folderView('backend', 'Backend', { path: [{ id: 'team', name: 'Team' }] })),
    });
    const { user } = renderApp('/folders/backend');
    const tree = await screen.findByRole('tree', { name: 'Folders and automations' });
    const build = await within(tree).findByRole('treeitem', { name: 'Build the shop' });
    expect(build).toHaveAttribute('aria-level', '3');
    expect(build).not.toHaveAttribute('aria-expanded');

    await user.click(within(build).getByRole('button', { name: /Build the shop/ }));
    expect(await within(tree).findByRole('treeitem', { name: 'Build the shop', selected: true })).toBeInTheDocument();
    expect(within(tree).getByRole('treeitem', { name: /Backend/ })).toHaveAttribute('aria-selected', 'false');
  });

  it('can be hidden', async () => {
    server();
    const { user } = renderApp('/');

    await user.click(await screen.findByRole('button', { name: 'Hide folders' }));

    expect(screen.queryByRole('tree')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Show folders' })).toHaveAttribute('aria-expanded', 'false');
  });
});
