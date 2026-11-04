import { screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { fakeServer, info, json, me, noContent, problem, requests } from '../../test/fakeServer';
import { renderApp } from '../../test/renderApp';

const posted = vi.hoisted(() => vi.fn<(action: string, fields: Record<string, string>) => void>());
vi.mock('../../lib/postForm', () => ({ postForm: posted }));

const app = {
  appId: 4242,
  slug: 'autocanvas-acme',
  name: 'AutoCanvas (acme)',
  ownerLogin: 'acme',
  htmlUrl: 'https://github.com/apps/autocanvas-acme',
  installUrl: 'https://github.com/apps/autocanvas-acme/installations/new',
  webAddress: 'https://github.com/',
  apiAddress: 'https://api.github.com/',
  webhookUrl: 'https://autocanvas.example.org/api/v1/forges/github/events',
  installations: [
    { id: 7, account: 'acme', accountType: 'Organization', allRepositories: false, suspended: false },
    { id: 8, account: 'ada', accountType: 'User', allRepositories: true, suspended: true },
  ],
  installationsError: null,
  createdAt: '2026-11-04T10:00:00Z',
  createdByName: 'ada',
};

const session = { 'GET /api/v1/info': () => json(info), 'GET /api/v1/me': () => json(me()) };

describe('the forges', () => {
  it('create the GitHub App by posting the manifest to GitHub', async () => {
    fakeServer({
      ...session,
      'GET /api/v1/forges/github': () => json({ app: null }),
      'POST /api/v1/forges/github/manifest': () =>
        json({
          action: 'https://github.com/organizations/acme/settings/apps/new?state=s',
          manifest: '{"name":"AutoCanvas"}',
          state: 's',
        }),
    });
    const { user } = renderApp('/admin/forges');

    expect(await screen.findByText('No GitHub App yet')).toBeInTheDocument();
    await user.type(screen.getByLabelText('Organization'), 'acme');
    await user.click(screen.getByRole('button', { name: 'Create GitHub App' }));

    await vi.waitFor(() =>
      expect(posted).toHaveBeenCalledWith('/api/v1/forges/github/manifest/launch', { state: 's' }),
    );
    const sent = requests.find((request) => request.method === 'POST');
    expect(await sent?.json()).toEqual({ organization: 'acme', webAddress: null });
  });

  it('say why GitHub cannot be asked, such as without a public address', async () => {
    fakeServer({
      ...session,
      'GET /api/v1/forges/github': () => json({ app: null }),
      'POST /api/v1/forges/github/manifest': () => problem(409, 'Set the instance public address first.'),
    });
    const { user } = renderApp('/admin/forges');

    await user.click(await screen.findByRole('button', { name: 'Create GitHub App' }));

    expect(await screen.findByText('Set the instance public address first.')).toBeInTheDocument();
  });

  it('enter an existing app, with its key from a file', async () => {
    fakeServer({
      ...session,
      'GET /api/v1/forges/github': () => json({ app: null }),
      'PUT /api/v1/forges/github': () => json({ app }),
    });
    const { user } = renderApp('/admin/forges');

    await user.click(await screen.findByRole('button', { name: 'Enter an existing app' }));
    const dialog = await screen.findByRole('dialog', { name: 'Enter an existing GitHub App' });
    await user.type(within(dialog).getByLabelText('App ID'), '4242');
    await user.upload(
      within(dialog).getByLabelText(/Or load the .pem file/),
      new File(['the key from the file'], 'app.pem'),
    );
    await user.type(within(dialog).getByLabelText('Webhook secret'), 'hook-secret');
    await user.click(within(dialog).getByRole('button', { name: 'Enter app' }));

    expect(await screen.findByRole('link', { name: 'AutoCanvas (acme)' })).toBeInTheDocument();
    const sent = requests.find((request) => request.method === 'PUT');
    expect(await sent?.json()).toEqual({
      appId: 4242,
      privateKey: 'the key from the file',
      webhookSecret: 'hook-secret',
      webAddress: null,
    });
  });

  it('show the app, its installations and events address, and remove it', async () => {
    fakeServer({
      ...session,
      'GET /api/v1/forges/github': () => json({ app }),
      'DELETE /api/v1/forges/github': () => noContent(),
    });
    const { user } = renderApp('/admin/forges');

    expect(await screen.findByRole('link', { name: 'AutoCanvas (acme)' })).toHaveAttribute('href', app.htmlUrl);
    expect(screen.getByText(app.webhookUrl)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Install on an account' })).toHaveAttribute('href', app.installUrl);
    const installations = within(screen.getByRole('list', { name: 'Installations' }));
    expect(installations.getByText('Chosen repositories')).toBeInTheDocument();
    expect(installations.getByText('Suspended')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Remove' }));
    await user.click(await screen.findByRole('button', { name: 'Remove app' }));

    expect(await screen.findByText('No GitHub App yet')).toBeInTheDocument();
  });

  it('warn while GitHub cannot send events, and say why installations could not be read', async () => {
    fakeServer({
      ...session,
      'GET /api/v1/forges/github': () =>
        json({ app: { ...app, webhookUrl: null, installations: [], installationsError: 'GitHub answered 401.' } }),
    });
    renderApp('/admin/forges');

    expect(await screen.findByText('GitHub cannot send events yet')).toBeInTheDocument();
    expect(screen.getByText('GitHub answered 401.')).toBeInTheDocument();
  });

  it('finish the app when GitHub sends the browser back with a code', async () => {
    let finished = false;
    fakeServer({
      ...session,
      'POST /api/v1/forges/github/created': () => {
        finished = true;
        return json({ app });
      },
      'GET /api/v1/forges/github': () => json({ app: finished ? app : null }),
    });
    renderApp('/admin/forges/github/created?code=c0de&state=st4te');

    expect(await screen.findByRole('link', { name: 'AutoCanvas (acme)' })).toBeInTheDocument();
    const sent = requests.filter((request) => request.method === 'POST');
    expect(sent).toHaveLength(1);
    expect(await sent[0]?.json()).toEqual({ code: 'c0de', state: 'st4te' });
  });

  it('say when coming back fails or there is nothing to finish', async () => {
    fakeServer({
      ...session,
      'POST /api/v1/forges/github/created': () => problem(400, 'The answer from GitHub does not belong to a manifest.'),
    });
    renderApp('/admin/forges/github/created?code=c0de&state=old');

    expect(await screen.findByText('The answer from GitHub does not belong to a manifest.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /create it again/ })).toHaveAttribute('href', '/admin/forges');
  });

  it('say there is nothing to finish without a code', async () => {
    fakeServer(session);
    renderApp('/admin/forges/github/created');

    expect(await screen.findByText('Nothing to finish here')).toBeInTheDocument();
  });
});
