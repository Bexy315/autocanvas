import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { fakeServer, info, json, me, noContent, problem } from '../test/fakeServer';
import { renderApp } from '../test/renderApp';
import { ErrorBoundary } from '../ui/ErrorBoundary';
import { sidebarKey } from './AppShell';

function signedIn(extra: Parameters<typeof fakeServer>[0] = {}) {
  let session = true;
  fakeServer({
    'GET /api/v1/info': () => json(info),
    'GET /api/v1/folders': () => json([]),
    'GET /api/v1/me': () => (session ? json(me()) : problem(401, 'Sign in first.')),
    'POST /api/v1/auth/sign-out': () => {
      session = false;
      return noContent();
    },
    ...extra,
  });
}

describe('the app shell', () => {
  it('shows the start page with breadcrumbs and the user', async () => {
    signedIn();
    renderApp('/');

    expect(await screen.findByRole('heading', { name: 'Automations' })).toBeInTheDocument();
    expect(await screen.findByRole('heading', { name: 'No folders yet' })).toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Breadcrumbs' })).toHaveTextContent('Automations');
    expect(screen.getByRole('link', { name: 'Automations' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('button', { name: 'Account menu for Ada Lovelace' })).toBeInTheDocument();
  });

  it('collapses the sidebar and remembers it', async () => {
    signedIn();
    const { user } = renderApp('/');

    await user.click(await screen.findByRole('button', { name: 'Collapse sidebar' }));

    expect(screen.getByRole('button', { name: 'Expand sidebar' })).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByText(/^Version/)).not.toBeInTheDocument();
    expect(localStorage.getItem(sidebarKey)).toBe('collapsed');
  });

  it('opens the account and switches the color scheme from the user menu', async () => {
    signedIn();
    const { user } = renderApp('/');

    await user.click(await screen.findByRole('button', { name: 'Account menu for Ada Lovelace' }));
    await user.click(screen.getByRole('menuitemradio', { name: 'Dark' }));
    expect(document.documentElement.dataset['theme']).toBe('dark');

    await user.click(screen.getByRole('button', { name: 'Account menu for Ada Lovelace' }));
    await user.click(screen.getByRole('menuitem', { name: 'Account' }));
    expect(await screen.findByRole('heading', { name: 'Account' })).toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Breadcrumbs' })).toHaveTextContent(/^Account$/);
  });

  it('signs out', async () => {
    signedIn();
    const { user } = renderApp('/account');

    await user.click(await screen.findByRole('button', { name: 'Account menu for Ada Lovelace' }));
    await user.click(screen.getByRole('menuitem', { name: 'Sign out' }));

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    expect(globalThis.location.pathname).toBe('/sign-in');
  });

  it('asks to sign in again when the session ends elsewhere', async () => {
    signedIn({ 'PUT /api/v1/me/password': () => problem(401, 'Sign in first.') });
    const { user } = renderApp('/account');

    await user.type(await screen.findByLabelText('Current password'), 'my password here');
    await user.type(screen.getByLabelText('New password'), 'a password of my own');
    await user.type(screen.getByLabelText('Repeat the new password'), 'a password of my own');
    await user.click(screen.getByRole('button', { name: 'Change password' }));

    expect(await screen.findByText('Your session has ended')).toBeInTheDocument();
    expect(globalThis.location.search).toBe('?expired=1&returnTo=%2Faccount');
  });

  it('changes the password from the account page', async () => {
    signedIn({ 'PUT /api/v1/me/password': () => noContent() });
    const { user } = renderApp('/account');

    await user.type(await screen.findByLabelText('Current password'), 'my password here');
    await user.type(screen.getByLabelText('New password'), 'a password of my own');
    await user.type(screen.getByLabelText('Repeat the new password'), 'a password of my own');
    await user.click(screen.getByRole('button', { name: 'Change password' }));

    expect(await screen.findByText('Password changed')).toBeInTheDocument();
    expect(screen.getByLabelText('Current password')).toHaveValue('');
  });

  it('says when a page does not exist', async () => {
    signedIn();
    renderApp('/no/such/page');

    expect(await screen.findByRole('heading', { name: 'There is no page here' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Go to automations' })).toHaveAttribute('href', '/');
  });
});

function Broken(): never {
  throw new Error('The canvas has no nodes.');
}

describe('a page that fails to render', () => {
  it('is replaced by an explanation with a way to reload', () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    render(
      <ErrorBoundary>
        <Broken />
      </ErrorBoundary>,
    );

    expect(screen.getByRole('alert')).toHaveTextContent('The canvas has no nodes.');
    expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument();
  });
});
