import { spawn, type ChildProcess } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

/** The server as `dotnet build -c Release` leaves it; it runs from its project so it finds the web UI. */
const project = resolve(import.meta.dirname, '../../AutoCanvas.Server');
const serverDll = join(project, 'bin/Release/net10.0/AutoCanvas.Server.dll');

/** A fresh server of its own, in setup mode: no account, no configured master key. */
export interface SetupServer {
  /** Where it is reached. */
  url: string;
  /** The setup token, read from the server's log as a person would. */
  token: string;
  /** Where it keeps its files. */
  dataDirectory: string;
  /** The file the setup hands the bundled runner's token to. */
  runnerTokenFile: string;
  stop(): Promise<void>;
}

/** Starts a fresh server on ports of its own and waits for the setup address in its log. */
export async function startSetupServer(port: number): Promise<SetupServer> {
  const root = mkdtempSync(join(tmpdir(), 'autocanvas-e2e-setup-'));
  const dataDirectory = join(root, 'data');
  const runnerTokenFile = join(root, 'handoff', 'runner-token');
  const child: ChildProcess = spawn(
    'dotnet',
    [
      serverDll,
      `--urls=http://127.0.0.1:${port}`,
      `--Runners:Urls=http://127.0.0.1:${port + 1}`,
      `--Server:DataDirectory=${dataDirectory}`,
      `--Setup:RunnerTokenFile=${runnerTokenFile}`,
    ],
    { cwd: project, env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Production' }, stdio: ['ignore', 'pipe', 'pipe'] },
  );
  const exited = new Promise<void>((done) => child.once('exit', () => done()));
  let output = '';
  const token = await new Promise<string>((found, failed) => {
    const timer = setTimeout(() => failed(new Error(`No setup address in the server's log:\n${output}`)), 120_000);
    const read = (chunk: Buffer) => {
      output += chunk.toString();
      const match = /setup\?token=([A-Za-z0-9_-]+)/.exec(output);
      if (match?.[1]) {
        clearTimeout(timer);
        found(match[1]);
      }
    };
    child.stdout?.on('data', read);
    child.stderr?.on('data', read);
    child.once('exit', () => {
      clearTimeout(timer);
      failed(new Error(`The server stopped:\n${output}`));
    });
  });
  return {
    url: `http://127.0.0.1:${port}`,
    token,
    dataDirectory,
    runnerTokenFile,
    async stop() {
      if (child.exitCode === null) {
        child.kill();
        await exited;
      }
      rmSync(root, { recursive: true, force: true });
    },
  };
}
