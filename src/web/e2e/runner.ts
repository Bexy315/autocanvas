import { spawn, spawnSync, type ChildProcess } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { expect, type APIRequestContext } from '@playwright/test';

const csrf = { 'X-AutoCanvas-Request': '1' };

/** The reference runner as `dotnet build -c Release` leaves it (CI builds it before the e2e tests). */
const runnerDll = resolve(import.meta.dirname, '../../AutoCanvas.Runner/bin/Release/net10.0/AutoCanvas.Runner.dll');

/** Whether Docker answers; the runner runs every job in a container. */
export function dockerAvailable(): boolean {
  return spawnSync('docker', ['info'], { stdio: 'ignore', timeout: 30_000 }).status === 0;
}

/** A reference runner registered with the e2e server, running until `stop`. */
export interface Runner {
  name: string;
  stop(): Promise<void>;
}

/**
 * Registers a reference runner with a registration token the admin creates through the API, then
 * runs it as a child process with a data directory of its own.
 */
export async function startRunner(request: APIRequestContext, name: string): Promise<Runner> {
  const created = await request.post('/api/v1/runner-registration-tokens', {
    data: { description: 'e2e walk-through', expiresAt: null },
    headers: csrf,
  });
  if (!created.ok()) {
    throw new Error(`No registration token: ${created.status()} ${await created.text()}`);
  }
  const { token } = (await created.json()) as { token: string };
  const dataDirectory = mkdtempSync(join(tmpdir(), 'autocanvas-e2e-runner-'));
  const env = {
    ...process.env,
    AUTOCANVAS_RUNNER__SERVER: 'http://127.0.0.1:5091',
    AUTOCANVAS_RUNNER__DATADIRECTORY: dataDirectory,
    AUTOCANVAS_RUNNER__LABELS__0: 'linux',
  };
  const registered = spawnSync('dotnet', [runnerDll, 'register', '--token', token, '--name', name], {
    env,
    encoding: 'utf8',
    timeout: 60_000,
  });
  if (registered.status !== 0) {
    throw new Error(`The runner did not register: ${registered.stderr || registered.stdout}`);
  }
  const child: ChildProcess = spawn('dotnet', [runnerDll], { env, stdio: ['ignore', 'ignore', 'pipe'] });
  let errors = '';
  child.stderr?.on('data', (chunk: Buffer) => {
    errors += chunk.toString();
  });
  const exited = new Promise<void>((done) => child.once('exit', () => done()));

  // Online once the server heard from it.
  let id = '';
  const online = async () => {
    if (child.exitCode !== null) {
      throw new Error(`The runner stopped: ${errors}`);
    }
    const runners = (await (await request.get('/api/v1/runners')).json()) as {
      items: { id: string; name: string; isOnline: boolean }[];
    };
    const found = runners.items.find((runner) => runner.name === name && runner.isOnline);
    id = found?.id ?? '';
    return found !== undefined;
  };
  try {
    await expect.poll(online, { timeout: 60_000, intervals: [500] }).toBe(true);
  } catch (error) {
    child.kill();
    throw error;
  }

  return {
    name,
    async stop() {
      if (child.exitCode === null) {
        child.kill();
        await exited;
      }
      removeLeftovers(id);
      rmSync(dataDirectory, { recursive: true, force: true });
    },
  };
}

/**
 * Removes what a runner's jobs left running when it was stopped: its sweeper would at its next
 * start, but a test's runner does not start again.
 */
function removeLeftovers(runnerId: string) {
  if (!runnerId) {
    return;
  }
  const left = spawnSync('docker', ['ps', '--all', '--quiet', '--filter', `label=autocanvas.runner=${runnerId}`], {
    encoding: 'utf8',
  })
    .stdout.split(/\s+/)
    .filter(Boolean);
  if (left.length > 0) {
    spawnSync('docker', ['rm', '--force', ...left], { stdio: 'ignore' });
  }
}

/**
 * The runner as the compose package runs it (ADR-0020): unregistered, it waits for the token the
 * setup writes to `tokenFile`, registers itself as `bundled` and runs.
 */
export function startBundledRunner(server: string, tokenFile: string): { stop(): Promise<void> } {
  const dataDirectory = mkdtempSync(join(tmpdir(), 'autocanvas-e2e-bundled-'));
  const child: ChildProcess = spawn('dotnet', [runnerDll], {
    env: {
      ...process.env,
      AUTOCANVAS_RUNNER__SERVER: server,
      AUTOCANVAS_RUNNER__DATADIRECTORY: dataDirectory,
      AUTOCANVAS_RUNNER__REGISTRATIONTOKENFILE: tokenFile,
      AUTOCANVAS_RUNNER__NAME: 'bundled',
      AUTOCANVAS_RUNNER__LABELS__0: 'linux',
    },
    stdio: 'ignore',
  });
  const exited = new Promise<void>((done) => child.once('exit', () => done()));
  return {
    async stop() {
      if (child.exitCode === null) {
        child.kill();
        await exited;
      }
      const credential = join(dataDirectory, 'credential.json');
      if (existsSync(credential)) {
        removeLeftovers((JSON.parse(readFileSync(credential, 'utf8')) as { runnerId: string }).runnerId);
      }
      rmSync(dataDirectory, { recursive: true, force: true });
    },
  };
}
