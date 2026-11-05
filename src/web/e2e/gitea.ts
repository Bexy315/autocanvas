import { spawnSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { expect } from '@playwright/test';

/** The Gitea image of the walk-through, pinned. */
export const giteaImage = 'gitea/gitea:1.24.6';

/**
 * Where containers and this machine both reach services on this machine: the Docker bridge's
 * gateway on Linux, Docker Desktop's name for the host elsewhere.
 */
export function dockerHost(): string {
  return process.platform === 'linux' ? '172.17.0.1' : 'host.docker.internal';
}

/**
 * A real Gitea in a container for the walk-through: a user with a token, private repositories, files
 * committed through its API (each a push), and webhooks. Started by the test and removed after it.
 */
export class Gitea {
  readonly user = 'ada';
  readonly password = `e2e-${randomBytes(8).toString('hex')}`;
  readonly name = `autocanvas-e2e-gitea-${randomBytes(4).toString('hex')}`;
  token = '';
  readonly port: number;

  constructor(port: number) {
    this.port = port;
  }

  /** The clone address, the same for the server and for the runner's containers. */
  cloneUrl(repository: string): string {
    return `http://${dockerHost()}:${this.port}/${this.user}/${repository}.git`;
  }

  private get api(): string {
    return `http://127.0.0.1:${this.port}/api/v1`;
  }

  async start(): Promise<void> {
    docker([
      'run',
      '--detach',
      '--name',
      this.name,
      '--publish',
      `${this.port}:3000`,
      '--env',
      'GITEA__database__DB_TYPE=sqlite3',
      '--env',
      'GITEA__security__INSTALL_LOCK=true',
      '--env',
      'GITEA__webhook__ALLOWED_HOST_LIST=*',
      '--env',
      `GITEA__server__ROOT_URL=http://${dockerHost()}:${this.port}/`,
      giteaImage,
    ]);
    await expect
      .poll(async () => (await fetch(`${this.api}/version`).catch(() => null))?.ok ?? false, {
        timeout: 120_000,
        intervals: [1000],
      })
      .toBe(true);
    docker([
      'exec',
      '--user',
      'git',
      this.name,
      'gitea',
      'admin',
      'user',
      'create',
      '--admin',
      '--username',
      this.user,
      '--password',
      this.password,
      '--email',
      'ada@example.org',
      '--must-change-password=false',
    ]);
    const token = (await this.call(
      'POST',
      `/users/${this.user}/tokens`,
      {
        name: 'autocanvas',
        scopes: ['write:repository', 'write:user'],
      },
      true,
    )) as { sha1: string };
    this.token = token.sha1;
  }

  stop(): void {
    spawnSync('docker', ['rm', '--force', this.name], { stdio: 'ignore', timeout: 60_000 });
  }

  /** A private repository with a first commit on `main`. */
  async createRepository(name: string): Promise<void> {
    await this.call('POST', '/user/repos', { name, private: true, auto_init: true, default_branch: 'main' });
  }

  /** Commits a file to `main` through the API, which Gitea treats as a push; the commit. */
  async commit(repository: string, path: string, content: string, message: string): Promise<string> {
    const existing = await fetch(`${this.api}/repos/${this.user}/${repository}/contents/${path}?ref=main`, {
      headers: this.headers(false),
    });
    const sha = existing.ok ? ((await existing.json()) as { sha: string }).sha : undefined;
    const body = { content: Buffer.from(content).toString('base64'), message, branch: 'main', ...(sha ? { sha } : {}) };
    const done = (await this.call(
      sha ? 'PUT' : 'POST',
      `/repos/${this.user}/${repository}/contents/${path}`,
      body,
    )) as {
      commit: { sha: string };
    };
    return done.commit.sha;
  }

  /** A webhook for pushes, of Gitea's own kind, signed with the secret. */
  async addWebhook(repository: string, url: string, secret: string): Promise<void> {
    await this.call('POST', `/repos/${this.user}/${repository}/hooks`, {
      type: 'gitea',
      config: { url, content_type: 'json', secret },
      events: ['push'],
      active: true,
    });
  }

  private headers(basic: boolean): Record<string, string> {
    return {
      'content-type': 'application/json',
      authorization: basic
        ? `Basic ${Buffer.from(`${this.user}:${this.password}`).toString('base64')}`
        : `token ${this.token}`,
    };
  }

  private async call(method: string, path: string, body: unknown, basic = false): Promise<unknown> {
    const response = await fetch(`${this.api}${path}`, {
      method,
      headers: this.headers(basic),
      body: JSON.stringify(body),
    });
    if (!response.ok) {
      throw new Error(`Gitea answered ${response.status} to ${method} ${path}: ${await response.text()}`);
    }
    return response.json();
  }
}

function docker(args: string[]): void {
  const result = spawnSync('docker', args, { encoding: 'utf8', timeout: 300_000 });
  if (result.status !== 0) {
    throw new Error(`docker ${args[0]} failed: ${result.stderr || result.stdout}`);
  }
}
