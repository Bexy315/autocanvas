import { screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { fakeServer, info, json, me, noContent, problem, requests } from '../test/fakeServer';
import { renderApp } from '../test/renderApp';

describe('signing in', () => {
  it('asks for a session first and returns to the page afterwards', async () => {
    let signedIn = false;
    fakeServer({
      'GET /api/v1/info': () => json(info),
      'GET /api/v1/me': () => (signedIn ? json(me()) : problem(401, 'Sign in first.')),
      'POST /api/v1/auth/sign-in': () => {
        signedIn = true;
        return json({ me: me(), expiresAt: '2026-11-02T00:00:00Z' });
      },
    });
    const { user } = renderApp('/account');

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    expect(globalThis.location.search).toBe('?returnTo=%2Faccount');
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeDisabled();
    await user.type(screen.getByLabelText('User name'), 'ada');
    await user.type(screen.getByLabelText('Password'), 'correct horse battery');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByRole('heading', { name: 'Account' })).toBeInTheDocument();
    expect(globalThis.location.pathname).toBe('/account');
    expect(screen.getByText('builders')).toBeInTheDocument();
    expect(screen.getByText('Version 1.0.0 (9dad45a)')).toBeInTheDocument();
    const signIn = requests.find((r) => r.url.endsWith('/auth/sign-in'));
    expect(await signIn?.json()).toEqual({ userName: 'ada', password: 'correct horse battery' });
  });

  it.each([
    [401, 'The user name or password is wrong'],
    [429, 'Too many attempts from this address'],
    [500, 'The database is down.'],
  ])('explains a refusal with %i and clears the password', async (status, message) => {
    fakeServer({
      'GET /api/v1/info': () => json(info),
      'GET /api/v1/me': () => problem(401, 'Sign in first.'),
      'POST /api/v1/auth/sign-in': () => problem(status, 'The database is down.'),
    });
    const { user } = renderApp('/sign-in');

    await user.type(await screen.findByLabelText('User name'), 'ada');
    await user.type(screen.getByLabelText('Password'), 'wrong');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(message);
    expect(screen.getByLabelText('Password')).toHaveValue('');
  });

  it('sends someone already signed in on to where they wanted to go', async () => {
    fakeServer({ 'GET /api/v1/info': () => json(info), 'GET /api/v1/me': () => json(me()) });
    renderApp('/sign-in?returnTo=%2Faccount');

    expect(await screen.findByRole('heading', { name: 'Account' })).toBeInTheDocument();
  });

  it('tries again when the server was not reachable yet', async () => {
    let up = false;
    fakeServer({
      'GET /api/v1/info': () => json(info),
      'GET /api/v1/me': () => (up ? json(me()) : problem(502, 'Bad Gateway')),
    });
    const { user } = renderApp('/');

    expect(await screen.findByRole('alert')).toHaveTextContent('may be starting or restarting');
    up = true;
    await user.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByRole('heading', { name: 'Automations' })).toBeInTheDocument();
  });

  it('shows why the account could not be loaded', async () => {
    fakeServer({ 'GET /api/v1/info': () => json(info), 'GET /api/v1/me': () => problem(500, 'The database is down.') });
    renderApp('/');

    expect(await screen.findByRole('alert')).toHaveTextContent('The database is down.');
  });
});

/** The API for a user whose password was set by someone else. */
function server(options: { refuse?: Response } = {}) {
  let mustChange = true;
  fakeServer({
    'GET /api/v1/info': () => json(info),
    'GET /api/v1/me': () => json(me({ mustChangePassword: mustChange })),
    'PUT /api/v1/me/password': () => {
      if (options.refuse) {
        return options.refuse;
      }
      mustChange = false;
      return noContent();
    },
  });
}

describe('a password set by someone else', () => {
  it('must be replaced before anything else', async () => {
    server();
    const { user } = renderApp('/account');

    expect(await screen.findByRole('heading', { name: 'Choose your own password' })).toBeInTheDocument();
    await user.type(screen.getByLabelText('Current password'), 'set by the admin');
    await user.type(screen.getByLabelText('New password'), 'a password of my own');
    await user.type(screen.getByLabelText('Repeat the new password'), 'a password of my own');
    await user.click(screen.getByRole('button', { name: 'Change password and continue' }));

    expect(await screen.findByRole('heading', { name: 'Account' })).toBeInTheDocument();
    expect(globalThis.location.pathname).toBe('/account');
  });

  it('is not sent when the two new passwords differ', async () => {
    server();
    const { user } = renderApp('/change-password');

    await user.type(await screen.findByLabelText('Current password'), 'set by the admin');
    await user.type(screen.getByLabelText('New password'), 'a password of my own');
    await user.type(screen.getByLabelText('Repeat the new password'), 'a different one');
    await user.click(screen.getByRole('button', { name: 'Change password and continue' }));

    expect(screen.getByLabelText('Repeat the new password')).toHaveAccessibleDescription(
      'The two new passwords differ.',
    );
    expect(requests.some((r) => r.method === 'PUT')).toBe(false);
  });

  it('shows what the server said about each field', async () => {
    server({ refuse: problem(400, 'The password needs at least 12 characters.', 'newPassword') });
    const { user } = renderApp('/change-password');

    await user.type(await screen.findByLabelText('Current password'), 'set by the admin');
    await user.type(screen.getByLabelText('New password'), 'short');
    await user.type(screen.getByLabelText('Repeat the new password'), 'short');
    await user.click(screen.getByRole('button', { name: 'Change password and continue' }));

    await waitFor(() =>
      expect(screen.getByLabelText('New password')).toHaveAccessibleDescription(
        '12 to 256 characters, and not your user name. The password needs at least 12 characters.',
      ),
    );
    expect(screen.getByLabelText('New password')).toBeInvalid();
  });

  it('shows other refusals above the form', async () => {
    server({ refuse: problem(500, 'The database is down.') });
    const { user } = renderApp('/change-password');

    await user.type(await screen.findByLabelText('Current password'), 'set by the admin');
    await user.type(screen.getByLabelText('New password'), 'a password of my own');
    await user.type(screen.getByLabelText('Repeat the new password'), 'a password of my own');
    await user.click(screen.getByRole('button', { name: 'Change password and continue' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('The database is down.');
  });
});
