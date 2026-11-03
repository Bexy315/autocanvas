import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { fakeServer, info, json, me, problem, requests } from '../../test/fakeServer';
import { renderApp } from '../../test/renderApp';

const settings = {
  publicAddress: { value: 'https://configured.example.org/', source: 'Configuration' },
  runnerAddress: { value: null, source: 'None' },
};

describe('the instance settings', () => {
  it('change the addresses the configuration does not set', async () => {
    let attempts = 0;
    fakeServer({
      'GET /api/v1/info': () => json(info),
      'GET /api/v1/me': () => json(me()),
      'GET /api/v1/instance': () => json(settings),
      'PUT /api/v1/instance': () => {
        attempts += 1;
        return attempts === 1
          ? problem(400, 'Give an absolute http or https address without a path.', 'runnerAddress')
          : json({ ...settings, runnerAddress: { value: 'http://runners.example.org:5081/', source: 'Database' } });
      },
    });
    const { user } = renderApp('/admin/instance');

    const publicAddress = await screen.findByLabelText('Public address');
    expect(publicAddress).toHaveValue('https://configured.example.org');
    expect(publicAddress).toHaveAttribute('readonly');
    expect(screen.getByText(/Set by the server's configuration/)).toBeInTheDocument();

    const runnerAddress = screen.getByLabelText('Runner address');
    await user.type(runnerAddress, 'ftp://wrong');
    await user.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('Give an absolute http or https address without a path.')).toBeInTheDocument();

    await user.clear(runnerAddress);
    await user.type(runnerAddress, 'http://runners.example.org:5081');
    await user.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('Saved.')).toBeInTheDocument();
    const sent = requests.filter((request) => request.method === 'PUT');
    expect(await sent[1]?.json()).toEqual({ publicAddress: null, runnerAddress: 'http://runners.example.org:5081' });
  });

  it('say when they cannot be read', async () => {
    fakeServer({
      'GET /api/v1/info': () => json(info),
      'GET /api/v1/me': () => json(me()),
      'GET /api/v1/instance': () => problem(500, 'The database is down.'),
    });
    renderApp('/admin/instance');

    expect(await screen.findByText('The instance settings could not be loaded')).toBeInTheDocument();
  });
});
