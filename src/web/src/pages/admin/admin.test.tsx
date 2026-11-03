import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { GrantView, SessionView } from '../../api/sharing';
import type { RunnerView, UserView } from '../../api/admin';
import { generatePassword } from '../../lib/password';
import { folderView, page } from '../../test/fixtures';
import { fakeServer, info, json, me, noContent, problem, requests, type Handler } from '../../test/fakeServer';
import { renderApp } from '../../test/renderApp';

const team = folderView('team', 'Team');

const user = (id: string, userName: string, extra: Partial<UserView> = {}): UserView => ({
  id,
  userName,
  displayName: userName.toUpperCase(),
  email: null,
  isAdmin: false,
  isDisabled: false,
  mustChangePassword: false,
  createdAt: '2026-11-01T10:00:00Z',
  lastSignInAt: null,
  ...extra,
});

const grant = (id: string, subjectType: 'user' | 'group', subjectName: string, role: GrantView['role']): GrantView => ({
  id,
  subjectType,
  subjectId: subjectType === 'user' && subjectName === 'ada' ? '00000000-0000-0000-0000-000000000001' : `${id}-subject`,
  subjectName,
  role,
  createdAt: '2026-11-01T10:00:00Z',
});

const runner = (id: string, name: string, extra: Partial<RunnerView> = {}): RunnerView => ({
  id,
  name,
  credentialHint: 'acr_…',
  capabilities: {
    labels: ['linux'],
    executor: 'Container',
    os: 'linux',
    arch: 'x64',
    nodeTypes: [],
    maxConcurrentJobs: 2,
    runnerVersion: '1.0.0',
    shells: ['bash', 'sh'],
  },
  isPaused: false,
  isOnline: true,
  lastSeenAt: '2026-11-02T10:00:00Z',
  createdAt: '2026-11-01T10:00:00Z',
  createdBy: 'ada',
  version: 1,
  ...extra,
});

/** A runner that never connected, so it announced nothing. */
const offline = Object.fromEntries(
  Object.entries(runner('r2', 'old', { isOnline: false, isPaused: true, lastSeenAt: null })).filter(
    ([key]) => key !== 'capabilities',
  ),
) as RunnerView;

function server(extra: Record<string, Handler> = {}) {
  fakeServer({
    'GET /api/v1/info': () => json(info),
    'GET /api/v1/me': () => json(me()),
    'GET /api/v1/folders': () => json([team]),
    'GET /api/v1/folders/team': () => json(team, 200, { ETag: '"3"' }),
    'GET /api/v1/folders/team/children': () => json(page([])),
    'GET /api/v1/folders/team/automations': () => json(page([])),
    'GET /api/v1/folders/team/grants': () =>
      json([grant('g1', 'user', 'ada', 'Owner'), grant('g2', 'group', 'readers', 'Viewer')]),
    'GET /api/v1/directory': () =>
      json({ users: [{ id: 'u-vic', userName: 'vic', displayName: 'Vic' }], groups: [{ id: 'g-ops', name: 'ops' }] }),
    'GET /api/v1/users': () =>
      json(
        page([
          user('u1', 'ada', { isAdmin: true, lastSignInAt: '2026-11-02T09:00:00Z' }),
          user('u2', 'vic', { isDisabled: true }),
        ]),
      ),
    'GET /api/v1/groups': () =>
      json(
        page([
          { id: 'grp', name: 'readers', description: 'who reads', createdAt: '2026-11-01T10:00:00Z', memberCount: 1 },
        ]),
      ),
    'GET /api/v1/groups/grp': () =>
      json({
        id: 'grp',
        name: 'readers',
        description: null,
        createdAt: '2026-11-01T10:00:00Z',
        members: [{ id: 'u2', userName: 'vic', displayName: 'Vic' }],
      }),
    'GET /api/v1/runners': () => json(page([runner('r1', 'build-1'), offline])),
    'GET /api/v1/runner-registration-tokens': () =>
      json([
        {
          id: 't1',
          description: 'basement',
          hint: 'acrt_a…z',
          createdAt: '2026-11-01T10:00:00Z',
          createdBy: 'ada',
          expiresAt: '2099-01-01T00:00:00Z',
          usedAt: null,
          runnerId: null,
          isUsable: true,
        },
      ]),
    'GET /api/v1/me/sessions': () =>
      json([
        {
          id: 's1',
          createdAt: '2026-11-02T10:00:00Z',
          lastSeenAt: '2026-11-02T10:00:00Z',
          expiresAt: '2026-11-09T10:00:00Z',
          clientAddress: '10.0.0.1',
          userAgent: 'Firefox',
          isCurrent: true,
        },
        {
          id: 's2',
          createdAt: '2026-11-01T10:00:00Z',
          lastSeenAt: '2026-11-01T10:00:00Z',
          expiresAt: '2026-11-08T10:00:00Z',
          clientAddress: null,
          userAgent: null,
          isCurrent: false,
        },
      ] satisfies SessionView[]),
    'GET /api/v1/me/tokens': () =>
      json([
        {
          id: 'k1',
          name: 'deploy',
          hint: 'acp_a…z',
          createdAt: '2026-11-01T10:00:00Z',
          expiresAt: null,
          lastUsedAt: null,
          isExpired: false,
        },
      ]),
    ...extra,
  });
}

describe('a generated password', () => {
  it('is long and avoids characters that look alike', () => {
    const password = generatePassword();
    expect(password).toHaveLength(20);
    expect(password).toMatch(/^[A-HJ-NP-Za-km-z2-9]+$/);
    expect(generatePassword(8)).toHaveLength(8);
    expect(generatePassword()).not.toBe(password);
  });
});

describe('sharing a folder', () => {
  it('lists who has access, changes a role, shares with someone found by name, and removes access', async () => {
    server({
      'PUT /api/v1/folders/team/grants': () => json(grant('g3', 'user', 'vic', 'Editor')),
      'DELETE /api/v1/folders/team/grants/g2': () => noContent(),
    });
    const { user: person } = renderApp('/folders/team/sharing');
    const access = await screen.findByRole('list', { name: 'Roles given here' });
    expect(within(access).getByText('(you)')).toBeInTheDocument();

    await person.selectOptions(within(access).getByLabelText('Role of readers'), 'Operator');
    let put = requests.findLast((request) => request.method === 'PUT');
    expect(await put?.json()).toEqual({ role: 'Operator', groupId: 'g2-subject' });

    await person.click(screen.getByRole('button', { name: 'Share' }));
    const dialog = await screen.findByRole('dialog', { name: 'Share Team' });
    await person.type(within(dialog).getByLabelText('Person or group'), 'vi');
    await person.click(await within(dialog).findByRole('button', { name: /^vic/ }));
    await person.click(within(dialog).getByLabelText(/^Editor/));
    await person.click(within(dialog).getByRole('button', { name: 'Share' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    put = requests.findLast((request) => request.method === 'PUT');
    expect(await put?.json()).toEqual({ role: 'Editor', userId: 'u-vic' });

    await person.click(within(access).getByRole('button', { name: 'Remove ada' }));
    expect(await screen.findByText(/This is your own Owner role/)).toBeInTheDocument();
    await person.click(screen.getByRole('button', { name: 'Cancel' }));
    await person.click(within(access).getByRole('button', { name: 'Remove readers' }));
    await person.click(await screen.findByRole('button', { name: 'Remove access' }));
    await waitFor(() => expect(requests.some((request) => request.method === 'DELETE')).toBe(true));
  });

  it('restricts the folder after asking, with the version it read', async () => {
    server({ 'PUT /api/v1/folders/team/restriction': () => json({ ...team, isRestricted: true }) });
    const { user: person } = renderApp('/folders/team/sharing');
    await person.click(await screen.findByRole('button', { name: 'Restrict' }));
    await person.click(
      within(await screen.findByRole('dialog', { name: 'Restrict Team?' })).getByRole('button', { name: 'Restrict' }),
    );
    await waitFor(() => expect(requests.some((request) => request.url.endsWith('/restriction'))).toBe(true));
    const put = requests.find((request) => request.url.endsWith('/restriction'));
    expect(put?.headers.get('If-Match')).toBe('"3"');
    expect(await put?.json()).toEqual({ isRestricted: true });
  });

  it('tells others that only owners see who has access', async () => {
    server({ 'GET /api/v1/folders/team': () => json(folderView('team', 'Team', { role: 'Editor' })) });
    renderApp('/folders/team/sharing');
    expect(await screen.findByText(/Only owners of the folder see who has access/)).toBeInTheDocument();
  });
});

describe('users', () => {
  it('are created with a password made up and shown once', async () => {
    server({ 'POST /api/v1/users': () => json(user('u3', 'grace'), 201) });
    const { user: person } = renderApp('/admin/users');
    const list = await screen.findByRole('list', { name: 'Users' });
    expect(within(list).getByText('Admin')).toBeInTheDocument();
    expect(within(list).getByText('Disabled')).toBeInTheDocument();

    await person.click(screen.getByRole('button', { name: 'New user' }));
    const dialog = await screen.findByRole('dialog', { name: 'New user' });
    await person.type(within(dialog).getByLabelText(/User name/), 'grace');
    await person.click(within(dialog).getByRole('button', { name: 'Create user' }));
    const shown = await screen.findByRole('dialog', { name: 'Password of grace' });
    const body = (await requests.find((request) => request.method === 'POST')?.json()) as { password: string };
    expect(within(shown).getByText(body.password)).toBeInTheDocument();
    expect(within(shown).getByRole('button', { name: 'Done' })).toBeDisabled();
    await person.click(within(shown).getByLabelText('I have passed the password on'));
    await person.click(within(shown).getByRole('button', { name: 'Done' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('are changed, have their password reset, their tokens revoked, and say why the last admin stays', async () => {
    server({
      'PATCH /api/v1/users/u1': () => problem(409, 'The last enabled admin cannot be demoted.'),
      'POST /api/v1/users/u2/password-reset': () => noContent(),
      'GET /api/v1/users/u2/tokens': () =>
        json([
          {
            id: 'k9',
            name: 'ci',
            hint: 'acp_…',
            createdAt: '2026-11-01T10:00:00Z',
            expiresAt: null,
            lastUsedAt: '2026-11-02T10:00:00Z',
            isExpired: true,
          },
        ]),
      'DELETE /api/v1/users/u2/tokens/k9': () => noContent(),
      'DELETE /api/v1/users/u2': () => noContent(),
    });
    const { user: person } = renderApp('/admin/users');
    await screen.findByRole('list', { name: 'Users' });

    await person.click(screen.getByRole('button', { name: 'Actions for ada' }));
    await person.click(await screen.findByRole('menuitem', { name: 'Change' }));
    const edit = await screen.findByRole('dialog', { name: 'Change ada' });
    await person.click(within(edit).getByLabelText('Admin of this instance'));
    await person.click(within(edit).getByRole('button', { name: 'Save' }));
    expect(await within(edit).findByText(/The last enabled admin cannot lose that role/)).toBeInTheDocument();
    await person.click(within(edit).getByRole('button', { name: 'Cancel' }));

    await person.click(screen.getByRole('button', { name: 'Actions for vic' }));
    await person.click(await screen.findByRole('menuitem', { name: 'Reset password' }));
    await person.click(await screen.findByRole('button', { name: 'Reset password' }));
    expect(await screen.findByRole('dialog', { name: 'Password of vic' })).toBeInTheDocument();
    await person.click(screen.getByLabelText('I have passed the password on'));
    await person.click(screen.getByRole('button', { name: 'Done' }));

    await person.click(screen.getByRole('button', { name: 'Actions for vic' }));
    await person.click(await screen.findByRole('menuitem', { name: 'API tokens' }));
    const tokens = await screen.findByRole('dialog', { name: 'API tokens of vic' });
    expect(await within(tokens).findByText('Expired')).toBeInTheDocument();
    await person.click(within(tokens).getByRole('button', { name: 'Revoke' }));
    await waitFor(() => expect(requests.some((request) => request.url.endsWith('/tokens/k9'))).toBe(true));
    await person.click(within(tokens).getAllByRole('button', { name: 'Close' })[0] ?? document.body);

    await person.click(screen.getByRole('button', { name: 'Actions for vic' }));
    await person.click(await screen.findByRole('menuitem', { name: 'Delete' }));
    await person.click(await screen.findByRole('button', { name: 'Delete user' }));
    await waitFor(() =>
      expect(requests.some((request) => request.method === 'DELETE' && request.url.endsWith('/users/u2'))).toBe(true),
    );
  });
});

describe('groups', () => {
  it('are created and given members found by name', async () => {
    server({
      'POST /api/v1/groups': () =>
        json({ id: 'new', name: 'ops', description: null, createdAt: '2026-11-02T10:00:00Z', members: [] }, 201),
      'PUT /api/v1/groups/grp/members/u-vic': () => noContent(),
      'DELETE /api/v1/groups/grp/members/u2': () => noContent(),
    });
    const { user: person } = renderApp('/admin/groups');
    await person.click(await screen.findByRole('button', { name: 'New group' }));
    const create = await screen.findByRole('dialog', { name: 'New group' });
    await person.type(within(create).getByLabelText(/Name/), 'ops');
    await person.click(within(create).getByRole('button', { name: 'Create group' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());

    await person.click(within(screen.getByRole('list', { name: 'Groups' })).getByRole('button', { name: 'readers' }));
    const members = await screen.findByRole('dialog', { name: 'Members of readers' });
    expect(await within(members).findByText('Vic')).toBeInTheDocument();
    await person.type(within(members).getByLabelText('Add a person'), 'vi');
    const match = await within(members).findByRole('button', { name: /^vic/ });
    // Arrow down moves from the field into the matches.
    within(members).getByLabelText('Add a person').focus();
    await person.keyboard('{ArrowDown}');
    expect(match).toHaveFocus();
    await person.keyboard('{Enter}');
    await waitFor(() => expect(requests.some((request) => request.url.endsWith('/members/u-vic'))).toBe(true));
    expect(within(members).queryByRole('button', { name: /^ops/ })).not.toBeInTheDocument();
    await person.click(within(members).getByRole('button', { name: 'Remove vic from readers' }));
    await waitFor(() => expect(requests.some((request) => request.method === 'DELETE')).toBe(true));
  });
});

describe('runners', () => {
  it('show whether they are online, pause, rename and delete', async () => {
    server({
      'PATCH /api/v1/runners/r1': () => json(runner('r1', 'build-1', { isPaused: true })),
      'DELETE /api/v1/runners/r2': () => noContent(),
    });
    const { user: person } = renderApp('/admin/runners');
    const list = await screen.findByRole('list', { name: 'Runners' });
    expect(within(list).getByText('Online')).toBeInTheDocument();
    expect(
      within(list).getByText(/labels linux · Container · linux\/x64 · 2 at a time · version 1\.0\.0/),
    ).toBeInTheDocument();
    expect(within(list).getByText('never connected')).toBeInTheDocument();

    await person.click(within(list).getByRole('button', { name: 'Pause' }));
    await waitFor(() => expect(requests.some((request) => request.method === 'PATCH')).toBe(true));
    expect(await requests.find((request) => request.method === 'PATCH')?.json()).toEqual({ isPaused: true });

    await person.click(within(list).getByRole('button', { name: 'Actions for build-1' }));
    await person.click(await screen.findByRole('menuitem', { name: 'Rename' }));
    const rename = await screen.findByRole('dialog', { name: 'Rename build-1' });
    await person.clear(within(rename).getByLabelText('Name'));
    await person.type(within(rename).getByLabelText('Name'), 'build-2');
    await person.click(within(rename).getByRole('button', { name: 'Rename' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());

    await person.click(within(list).getByRole('button', { name: 'Actions for old' }));
    await person.click(await screen.findByRole('menuitem', { name: 'Delete' }));
    await person.click(await screen.findByRole('button', { name: 'Delete runner' }));
    await waitFor(() => expect(requests.some((request) => request.method === 'DELETE')).toBe(true));
  });

  it('are registered with a token shown once, with the commands that use it', async () => {
    server({
      'POST /api/v1/runner-registration-tokens': () =>
        json(
          {
            token: 'acrt_secret',
            details: {
              id: 't2',
              description: null,
              hint: 'acrt_s…t',
              createdAt: '2026-11-02T10:00:00Z',
              createdBy: 'ada',
              expiresAt: null,
              usedAt: null,
              runnerId: null,
              isUsable: true,
            },
          },
          201,
        ),
      'DELETE /api/v1/runner-registration-tokens/t1': () => noContent(),
      'GET /api/v1/instance': () =>
        json({
          publicAddress: { value: null, source: 'None' },
          runnerAddress: { value: 'http://runners.example.org:5081/', source: 'Database' },
        }),
    });
    const { user: person } = renderApp('/admin/runners');
    await person.click(await screen.findByRole('button', { name: 'Register a runner' }));
    const create = await screen.findByRole('dialog', { name: 'Register a runner' });
    await person.selectOptions(within(create).getByLabelText('Usable until'), '7');
    await person.click(within(create).getByRole('button', { name: 'Create token' }));
    const shown = await screen.findByRole('dialog', { name: 'Runner token created' });
    expect(within(shown).getAllByText(/register --token acrt_secret --name build-1/, { selector: 'pre' })).toHaveLength(
      2,
    );
    // The address set for the instance, without the server's slash.
    const address = within(shown).getByLabelText(/runner address/);
    expect(address).toHaveValue('http://runners.example.org:5081');
    await person.clear(address);
    await person.type(address, 'http://ci.example.org:5081');
    expect(within(shown).getAllByText(/ci\.example\.org:5081/).length).toBeGreaterThan(0);
    await person.click(within(shown).getByLabelText('I have used or saved the token'));
    await person.click(within(shown).getByRole('button', { name: 'Done' }));

    await person.click(await screen.findByRole('button', { name: 'Delete the token acrt_a…z' }));
    await waitFor(() => expect(requests.some((request) => request.method === 'DELETE')).toBe(true));
  });
});

describe('one’s own account', () => {
  it('lists where one is signed in and ends another session', async () => {
    server({ 'DELETE /api/v1/me/sessions/s2': () => noContent() });
    const { user: person } = renderApp('/account');
    const sessions = await screen.findByRole('list', { name: 'Sessions' });
    expect(await within(sessions).findByText('This browser')).toBeInTheDocument();
    expect(within(sessions).getByText('Unknown browser')).toBeInTheDocument();
    await person.click(within(sessions).getByRole('button', { name: 'Sign out there' }));
    await waitFor(() => expect(requests.some((request) => request.method === 'DELETE')).toBe(true));
  });

  it('creates an API token shown once, and deletes one', async () => {
    server({
      'POST /api/v1/me/tokens': () =>
        json(
          {
            token: 'acp_secret',
            details: {
              id: 'k2',
              name: 'cli',
              hint: 'acp_s…t',
              createdAt: '2026-11-02T10:00:00Z',
              expiresAt: null,
              lastUsedAt: null,
              isExpired: false,
            },
          },
          201,
        ),
      'DELETE /api/v1/me/tokens/k1': () => noContent(),
    });
    const { user: person } = renderApp('/account');
    await person.click(await screen.findByRole('button', { name: 'New token' }));
    const create = await screen.findByRole('dialog', { name: 'New API token' });
    await person.type(within(create).getByLabelText(/What it is for/), 'cli');
    await person.selectOptions(within(create).getByLabelText('Expires'), '0');
    await person.click(within(create).getByRole('button', { name: 'Create token' }));
    expect(await requests.find((request) => request.method === 'POST')?.json()).toEqual({
      name: 'cli',
      expiresAt: null,
    });
    const shown = await screen.findByRole('dialog', { name: 'API token created' });
    expect(within(shown).getByText('acp_secret')).toBeInTheDocument();
    await person.click(within(shown).getByLabelText('I have saved the token'));
    await person.click(within(shown).getByRole('button', { name: 'Done' }));

    await person.click(await screen.findByRole('button', { name: 'Delete the token deploy' }));
    await person.click(await screen.findByRole('button', { name: 'Delete token' }));
    await waitFor(() => expect(requests.some((request) => request.method === 'DELETE')).toBe(true));
  });
});
