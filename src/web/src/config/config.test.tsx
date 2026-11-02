import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ConfigurationView, VariableView } from '../api/configuration';
import { automationView, folderView, page } from '../test/fixtures';
import { fakeServer, info, json, me, noContent, problem, requests, type Handler } from '../test/fakeServer';
import { renderApp } from '../test/renderApp';
import { curlExample, powerShellExample } from './webhookExamples';

describe('webhook examples', () => {
  it('call the address unsigned, or signed with the secret', () => {
    expect(curlExample('http://x/api/v1/hooks/ach_1', null)).toBe(
      "curl -X POST 'http://x/api/v1/hooks/ach_1' \\\n  -H 'Content-Type: application/json' \\\n  -d '{\"reason\":\"a test call\"}'",
    );
    const signed = curlExample('http://x/h', "it's");
    expect(signed).toContain("-hmac 'it'\\''s'");
    expect(signed).toContain('-H "X-AutoCanvas-Signature: $signature"');
    expect(powerShellExample('http://x/h', null)).toBe(
      "$body = '{\"reason\":\"a test call\"}'\nInvoke-RestMethod -Method Post -Uri 'http://x/h' -ContentType 'application/json' -Body $body",
    );
    const ps = powerShellExample('http://x/h', "it's");
    expect(ps).toContain("GetBytes('it''s')");
    expect(ps).toContain("-Headers @{ 'X-AutoCanvas-Signature' = $signature }");
  });
});

const team = folderView('team', 'Team');
const variable = (name: string, value: string | null): VariableView => ({
  name,
  value,
  updatedAt: '2026-11-02T10:00:00Z',
  updatedByName: 'ada',
});

const configuration: ConfigurationView = {
  variables: [
    {
      name: 'CHANNEL',
      definitions: [
        { kind: 'Instance', folderId: null, folderName: null, value: 'beta' },
        { kind: 'Folder', folderId: 'team', folderName: 'Team', value: 'stable' },
      ],
      winner: { kind: 'Folder', folderId: 'team', folderName: 'Team', value: 'stable' },
    },
    {
      name: 'REGION',
      definitions: [{ kind: 'Automation', folderId: null, folderName: null, value: 'eu-west' }],
      winner: { kind: 'Automation', folderId: null, folderName: null, value: 'eu-west' },
    },
  ],
  secrets: [
    {
      name: 'TOKEN',
      definitions: [{ kind: 'Automation', folderId: null, folderName: null, value: null }],
      winner: { kind: 'Automation', folderId: null, folderName: null, value: null },
    },
  ],
};

const triggers = {
  manual: false,
  webhook: true,
  schedules: [
    { position: 0, schedule: '0 6 * * 1-5', timeZone: 'Europe/Berlin', nextAt: '2099-01-01T06:00:00Z', lastAt: null },
    { position: 1, schedule: 'every 5m', timeZone: null, nextAt: null, lastAt: '2026-11-02T09:00:00Z' },
  ],
  follows: { folderId: 'team', automation: 'build', on: 'Failure' },
};

function server(extra: Record<string, Handler> = {}) {
  fakeServer({
    'GET /api/v1/info': () => json(info),
    'GET /api/v1/me': () => json(me()),
    'GET /api/v1/folders': () => json([team]),
    'GET /api/v1/folders/team': () => json(team, 200, { ETag: '"3"' }),
    'GET /api/v1/folders/team/children': () => json(page([])),
    'GET /api/v1/folders/team/automations': () => json(page([])),
    'GET /api/v1/automations/a1': () => json(automationView('a1', 'team'), 200, { ETag: '"2"' }),
    'GET /api/v1/automations/a1/triggers': () => json(triggers),
    'GET /api/v1/automations/a1/webhook': () =>
      json({ enabled: true, hint: null, requiresSignature: false, createdAt: null, createdByName: null }),
    'GET /api/v1/automations/a1/configuration': () => json(configuration),
    'GET /api/v1/automations/a1/secrets': () =>
      json([{ name: 'TOKEN', updatedAt: '2026-11-02T10:00:00Z', updatedByName: 'ada' }]),
    'GET /api/v1/folders/team/configuration': () => json(configuration),
    'GET /api/v1/folders/team/variables': () => json([variable('CHANNEL', 'stable'), variable('HIDDEN', null)]),
    'GET /api/v1/folders/team/secrets': () => json([]),
    'GET /api/v1/variables': () => json([variable('CHANNEL', 'beta')]),
    'GET /api/v1/secrets': () => json([]),
    ...extra,
  });
}

describe('the triggers of an automation', () => {
  it('show schedules with their times and the automation it follows', async () => {
    server();
    renderApp('/automations/a1/triggers');
    const list = await screen.findByRole('list', { name: 'Triggers' });
    const schedules = within(list).getByRole('list', { name: 'Schedules' });
    expect(within(schedules).getByText(/0 6 \* \* 1-5/)).toBeInTheDocument();
    expect(within(schedules).getByText(/never again/)).toBeInTheDocument();
    expect(within(schedules).getByText(/not yet/)).toBeInTheDocument();
    expect(within(list).getByRole('link', { name: 'build' })).toHaveAttribute('href', '/folders/team');
    expect(within(list).getByText(/fails\./)).toBeInTheDocument();
    expect(within(list).getByText(/manual: true/)).toBeInTheDocument();
  });

  it('create a signed webhook address, shown once until saved, then rotate and delete it', async () => {
    let created = false;
    server({
      'GET /api/v1/automations/a1/webhook': () =>
        json(
          created
            ? {
                enabled: true,
                hint: 'ach_ab…yz',
                requiresSignature: true,
                createdAt: '2026-11-02T10:00:00Z',
                createdByName: 'ada',
              }
            : { enabled: true, hint: null, requiresSignature: false, createdAt: null, createdByName: null },
        ),
      'POST /api/v1/automations/a1/webhook': () => {
        created = true;
        return json(
          {
            path: '/api/v1/hooks/ach_abcdefyz',
            token: 'ach_abcdefyz',
            signingSecret: 'shh',
            hint: 'ach_ab…yz',
            enabled: true,
          },
          201,
        );
      },
      'DELETE /api/v1/automations/a1/webhook': () => {
        created = false;
        return noContent();
      },
    });
    const { user } = renderApp('/automations/a1/triggers');
    await user.click(await screen.findByRole('button', { name: 'Create address' }));
    const dialog = await screen.findByRole('dialog', { name: 'Webhook address created' });
    expect(within(dialog).getByText(`${globalThis.location.origin}/api/v1/hooks/ach_abcdefyz`)).toBeInTheDocument();
    expect(within(dialog).getByText('shh')).toBeInTheDocument();
    expect(within(dialog).getByLabelText('Example call with curl')).toHaveTextContent('-hmac');
    await user.click(within(dialog).getByRole('button', { name: 'PowerShell' }));
    expect(within(dialog).getByLabelText('Example call with PowerShell')).toHaveTextContent('HMACSHA256');

    // Esc does not lose the values; once saved, Done closes.
    await user.keyboard('{Escape}');
    expect(screen.getByRole('dialog', { name: 'Webhook address created' })).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Done' })).toBeDisabled();
    await user.click(within(dialog).getByLabelText('I have saved the address and the secret'));
    await user.click(within(dialog).getByRole('button', { name: 'Done' }));
    expect(await screen.findByText('Signed calls only')).toBeInTheDocument();
    const call = requests.find((request) => request.method === 'POST');
    expect(await call?.json()).toEqual({ requireSignature: true });

    await user.click(screen.getByRole('button', { name: 'Rotate' }));
    await user.click(await screen.findByRole('button', { name: 'Create a new address' }));
    await user.click(await screen.findByLabelText('I have saved the address and the secret'));
    await user.click(screen.getByRole('button', { name: 'Done' }));

    await user.click(screen.getByRole('button', { name: 'Delete' }));
    await user.click(await screen.findByRole('button', { name: 'Delete address' }));
    expect(await screen.findByRole('button', { name: 'Create address' })).toBeInTheDocument();
  });

  it('let viewers see the address but not change it', async () => {
    server({ 'GET /api/v1/automations/a1': () => json(automationView('a1', 'team', { role: 'Viewer' })) });
    renderApp('/automations/a1/triggers');
    expect(await screen.findByText(/someone with the Operator role creates one/)).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Change in the editor' })).not.toBeInTheDocument();
  });
});

describe('the configuration of an automation', () => {
  it('shows its secrets, the variables of its file, and where every value comes from', async () => {
    server();
    const { user } = renderApp('/automations/a1/configuration');
    const secrets = await screen.findByRole('region', { name: 'Secrets' });
    expect(await within(secrets).findByText('TOKEN')).toBeInTheDocument();
    const file = screen.getByRole('region', { name: 'Variables in the file' });
    expect(await within(file).findByText('eu-west')).toBeInTheDocument();

    const explained = await screen.findByRole('list', { name: 'Variables and where they come from' });
    expect(within(explained).getByText('from')).toBeInTheDocument();
    expect(within(explained).getByRole('link', { name: 'folder Team' })).toHaveAttribute(
      'href',
      '/folders/team/configuration',
    );
    await user.click(within(explained).getByRole('button', { name: 'Every definition of CHANNEL' }));
    const chain = screen.getByRole('list', { name: 'Definitions of CHANNEL, from the instance down' });
    expect(within(chain).getByText('beta')).toHaveClass('font-mono');
    expect(
      within(screen.getByRole('list', { name: 'Secrets and where they come from' })).getByText('this automation'),
    ).toBeInTheDocument();

    await user.type(screen.getByRole('searchbox', { name: 'Find a name' }), 'reg');
    expect(
      within(screen.getByRole('list', { name: 'Variables and where they come from' })).queryByText('CHANNEL'),
    ).not.toBeInTheDocument();
  });

  it('sets a secret without ever showing its value', async () => {
    server({
      'PUT /api/v1/automations/a1/secrets/API_KEY': () =>
        json({ name: 'API_KEY', updatedAt: '2026-11-02T10:00:00Z', updatedByName: 'ada' }, 201),
    });
    const { user } = renderApp('/automations/a1/configuration');
    await user.click(await screen.findByRole('button', { name: 'Add secret' }));
    const dialog = await screen.findByRole('dialog', { name: 'Add a secret' });
    await user.type(within(dialog).getByLabelText('Name'), '1bad');
    expect(within(dialog).getByText('Letters, digits and _, not starting with a digit.')).toBeInTheDocument();
    await user.clear(within(dialog).getByLabelText('Name'));
    await user.type(within(dialog).getByLabelText('Name'), 'API_KEY');
    expect(within(dialog).getByLabelText('Value')).toHaveAttribute('type', 'password');
    await user.type(within(dialog).getByLabelText('Value'), 'very-secret');
    await user.click(within(dialog).getByRole('button', { name: 'Add secret' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    const put = requests.find((request) => request.method === 'PUT');
    expect(await put?.json()).toEqual({ value: 'very-secret' });
    expect(screen.queryByText('very-secret')).not.toBeInTheDocument();
  });
});

describe('the configuration of a folder', () => {
  it('changes and deletes variables, shows server problems at the field, and hides values from viewers', async () => {
    server({
      'PUT /api/v1/folders/team/variables/CHANNEL': () => json(variable('CHANNEL', 'edge')),
      'PUT /api/v1/folders/team/variables/NEW': () => problem(400, 'The name is reserved.', 'name'),
      'DELETE /api/v1/folders/team/variables/CHANNEL': () => noContent(),
    });
    const { user } = renderApp('/folders/team/configuration');
    expect(await screen.findByRole('navigation', { name: 'Folder' })).toBeInTheDocument();
    const variables = await screen.findByRole('region', { name: 'Variables' });
    expect(await within(variables).findByText('stable')).toBeInTheDocument();
    expect(within(variables).getByText('Hidden: values need the Operator role')).toBeInTheDocument();

    await user.click(within(variables).getByRole('button', { name: 'Change CHANNEL' }));
    const change = await screen.findByRole('dialog', { name: 'Change the variable CHANNEL' });
    await user.clear(within(change).getByLabelText('Value'));
    await user.type(within(change).getByLabelText('Value'), 'edge');
    await user.click(within(change).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());

    await user.click(within(variables).getByRole('button', { name: 'Add variable' }));
    const add = await screen.findByRole('dialog', { name: 'Add a variable' });
    await user.type(within(add).getByLabelText('Name'), 'NEW');
    await user.click(within(add).getByRole('button', { name: 'Add variable' }));
    expect(await within(add).findByText('The name is reserved.')).toBeInTheDocument();
    await user.click(within(add).getByRole('button', { name: 'Cancel' }));

    await user.click(within(variables).getByRole('button', { name: 'Delete CHANNEL' }));
    await user.click(await screen.findByRole('button', { name: 'Delete variable' }));
    await waitFor(() => expect(requests.some((request) => request.method === 'DELETE')).toBe(true));

    await user.click(screen.getByRole('link', { name: 'Contents' }));
    expect(await screen.findByText(/This folder is empty|Nothing here yet|empty/i)).toBeInTheDocument();
  });
});

describe('administration', () => {
  it('is in the sidebar for admins, with the instance variables', async () => {
    server();
    const { user } = renderApp('/');
    await user.click(await screen.findByRole('link', { name: 'Administration' }));
    expect(await screen.findByRole('heading', { name: 'Administration', level: 1 })).toBeInTheDocument();
    expect(await within(screen.getByRole('region', { name: 'Variables' })).findByText('beta')).toBeInTheDocument();
  });

  it('is not there for others', async () => {
    server({ 'GET /api/v1/me': () => json({ ...me(), user: { ...me().user, isAdmin: false } }) });
    renderApp('/admin');
    expect(await screen.findByRole('heading', { name: 'There is no page here' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Administration' })).not.toBeInTheDocument();
  });
});
