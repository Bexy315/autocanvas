import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { SetupCompleted, SetupDetails } from '../../api/setup';
import { fakeServer, info, json, me, problem, requests, type Handler } from '../../test/fakeServer';
import { renderApp } from '../../test/renderApp';
import { passwordProblem, stepOfField } from './setupModel';

const token = 'the-right-token';
const setupRequired = problem(503, 'Not set up.', undefined, {
  type: 'https://bexy315.github.io/autocanvas/technical/api#setup-required',
});

const details = (overrides: Partial<SetupDetails> = {}): SetupDetails => ({
  publicAddress: { value: null, source: 'None' },
  runnerAddress: { value: null, source: 'None' },
  bundledRunner: true,
  masterKey: 'Generated',
  ...overrides,
});

const completed = (overrides: Partial<SetupCompleted> = {}): SetupCompleted => ({
  me: me({ displayName: 'Felix' }),
  masterKey: { key: 'AAAAkey=', file: '/var/lib/autocanvas/keys/master.key' },
  runnerTokenWritten: true,
  exampleAutomationId: '00000000-0000-0000-0000-0000000000aa',
  ...overrides,
});

const online = {
  id: 'r1',
  name: 'bundled',
  credentialHint: 'acr_…',
  capabilities: null,
  isPaused: false,
  isOnline: true,
  lastSeenAt: '2026-11-03T10:00:00Z',
  createdAt: '2026-11-03T10:00:00Z',
  createdBy: 'setup',
  version: 1,
};

/** A fresh server: the setup with `found` behind the token, everything else "setup required". */
function freshServer(found: SetupDetails, complete: Handler, runners: () => unknown[] = () => [online]) {
  fakeServer({
    'GET /api/v1/info': () => json(info),
    'GET /api/v1/me': () => setupRequired.clone(),
    'GET /api/v1/setup': (request) => {
      const given = request.headers.get('X-AutoCanvas-Setup-Token');
      if (given === null) {
        return json({ open: true, details: null });
      }
      return given === token ? json({ open: true, details: found }) : problem(401, 'Wrong token.');
    },
    'POST /api/v1/setup': complete,
    'GET /api/v1/runners': () => json({ items: runners(), next: null }),
  });
}

describe('the setup', () => {
  it('leads from any page to the setup, checks the token, the admin and the addresses, and sets up', async () => {
    let attempts = 0;
    freshServer(
      details({ runnerAddress: { value: 'http://runners.example.org:5081/', source: 'Configuration' } }),
      () => {
        attempts += 1;
        return attempts === 1 ? problem(409, "The user name 'root' is taken.", 'admin.userName') : json(completed());
      },
    );
    const { user } = renderApp('/automations');

    expect(await screen.findByRole('heading', { name: 'Set up AutoCanvas' })).toBeInTheDocument();
    expect(globalThis.location.pathname).toBe('/setup');
    const steps = screen.getByRole('list', { name: 'Setup steps' });
    expect(within(steps).getByText('Token').closest('li')).toHaveAttribute('aria-current', 'step');

    // The token, typed in: an empty one, a wrong one, then the right one.
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    expect(screen.getByText('Enter the setup token.')).toBeInTheDocument();
    await user.type(screen.getByLabelText('Setup token'), 'wrong');
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    expect(await screen.findByText(/not this server's setup token/)).toBeInTheDocument();
    expect(globalThis.location.pathname).toBe('/setup');
    await user.clear(screen.getByLabelText('Setup token'));
    await user.type(screen.getByLabelText('Setup token'), token);
    await user.click(screen.getByRole('button', { name: 'Continue' }));

    // The admin, checked before going on.
    await user.type(await screen.findByLabelText('User name'), 'root');
    await user.type(screen.getByLabelText('Password'), 'short');
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    expect(screen.getByText('Use at least 12 characters.')).toBeInTheDocument();
    await user.clear(screen.getByLabelText('Password'));
    await user.type(screen.getByLabelText('Password'), 'a long enough password');
    await user.type(screen.getByLabelText('Repeat the password'), 'a long enough passwort');
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    expect(screen.getByText('The two passwords differ.')).toBeInTheDocument();
    await user.clear(screen.getByLabelText('Repeat the password'));
    await user.type(screen.getByLabelText('Repeat the password'), 'a long enough password');
    await user.click(screen.getByRole('button', { name: 'Continue' }));

    // The addresses: from the browser, or locked by the configuration.
    expect(screen.getByLabelText('Public address')).toHaveValue(globalThis.location.origin);
    expect(screen.getByLabelText('Runner address')).toHaveValue('http://runners.example.org:5081');
    expect(screen.getByLabelText('Runner address')).toHaveAttribute('readonly');
    expect(screen.getByText(/Set by the server's configuration/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Continue' }));

    // The name is taken: back to the admin, with the server's word at the field.
    await user.click(screen.getByRole('button', { name: 'Set up AutoCanvas' }));
    expect(await screen.findByText("The user name 'root' is taken.")).toBeInTheDocument();
    await user.clear(screen.getByLabelText('User name'));
    await user.type(screen.getByLabelText('User name'), 'felix');
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    await user.click(screen.getByRole('button', { name: 'Set up AutoCanvas' }));

    // The master key, until it is stored; then the runner, online; then into the app.
    expect(await screen.findByRole('heading', { name: 'Back up the master key' })).toBeInTheDocument();
    expect(screen.getByText('AAAAkey=')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Continue' })).toBeDisabled();
    await user.click(screen.getByLabelText('I have stored the master key'));
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    expect(await screen.findByRole('heading', { name: 'AutoCanvas is set up' })).toBeInTheDocument();
    expect(screen.getByText('The runner bundled is online.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open the example' })).toHaveAttribute(
      'href',
      '/automations/00000000-0000-0000-0000-0000000000aa',
    );

    const sent = requests.filter((request) => request.method === 'POST' && request.url.endsWith('/api/v1/setup'));
    expect(sent[1]?.headers.get('X-AutoCanvas-Setup-Token')).toBe(token);
    expect(await sent[1]?.json()).toEqual({
      admin: { userName: 'felix', password: 'a long enough password', displayName: null, email: null },
      publicAddress: globalThis.location.origin,
      runnerAddress: null,
      bundledRunner: true,
      example: true,
    });
  });

  it('takes the token from the address and drops it from there', async () => {
    freshServer(details({ bundledRunner: false }), () =>
      json(completed({ masterKey: null, runnerTokenWritten: false, exampleAutomationId: null })),
    );
    const { user } = renderApp(`/setup?token=${token}`);

    expect(await screen.findByLabelText('User name')).toBeInTheDocument();
    expect(globalThis.location.search).toBe('');
    await user.type(screen.getByLabelText('User name'), 'felix');
    await user.type(screen.getByLabelText('Password'), 'a long enough password');
    await user.type(screen.getByLabelText('Repeat the password'), 'a long enough password');
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    await user.click(screen.getByRole('button', { name: 'Continue' }));

    // No bundled runner to offer; without the example, the setup ends in the app.
    expect(screen.queryByLabelText('Register the bundled runner')).not.toBeInTheDocument();
    await user.click(screen.getByLabelText('Create an example automation'));
    await user.click(screen.getByRole('button', { name: 'Set up AutoCanvas' }));
    expect(await screen.findByRole('heading', { name: 'AutoCanvas is set up' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Go to AutoCanvas' })).toHaveAttribute('href', '/');
    const sent = requests.find((request) => request.method === 'POST');
    expect(await sent?.json()).toMatchObject({ bundledRunner: false, example: false });
  });

  it('goes on without the bundled runner when asked to', async () => {
    freshServer(
      details(),
      () => json(completed({ masterKey: null })),
      () => [],
    );
    const { user } = renderApp(`/setup?token=${token}`);

    await user.type(await screen.findByLabelText('User name'), 'felix');
    await user.type(screen.getByLabelText('Password'), 'a long enough password');
    await user.type(screen.getByLabelText('Repeat the password'), 'a long enough password');
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    await user.click(screen.getByRole('button', { name: 'Set up AutoCanvas' }));

    expect(await screen.findByRole('heading', { name: 'Waiting for the bundled runner' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Continue without waiting' }));
    expect(await screen.findByRole('heading', { name: 'AutoCanvas is set up' })).toBeInTheDocument();
  });

  it('sends people to the app once the installation is set up', async () => {
    fakeServer({
      'GET /api/v1/info': () => json(info),
      'GET /api/v1/me': () => problem(401, 'Sign in first.'),
      'GET /api/v1/setup': () => json({ open: false, details: null }),
    });
    renderApp('/setup');

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
  });

  it('leads from the sign-in to the setup too', async () => {
    freshServer(details(), () => json(completed()));
    renderApp('/sign-in');

    expect(await screen.findByRole('heading', { name: 'Set up AutoCanvas' })).toBeInTheDocument();
  });
});

describe('the setup model', () => {
  it.each([
    ['admin.password', 'admin'],
    ['publicAddress', 'addresses'],
    ['runnerAddress', 'addresses'],
    ['bundledRunner', 'options'],
    ['example', null],
    [null, null],
  ])('goes back to the step of %s', (field, step) => {
    expect(stepOfField(field)).toBe(step);
  });

  it.each([
    ['short', 'short', 'ada', 'Use at least 12 characters.'],
    ['Ada-Lovelace', 'Ada-Lovelace', 'ada-lovelace', 'The password must not be the user name.'],
    ['long enough one', 'long enough two', 'ada', 'The two passwords differ.'],
    ['long enough one', 'long enough one', 'ada', null],
  ])('checks the password %s', (password, repeated, userName, message) => {
    expect(passwordProblem(password, repeated, userName)).toBe(message);
  });
});
