import { generateKeyPairSync } from 'node:crypto';
import { createServer, type IncomingMessage, type Server } from 'node:http';

/** A repository the app's installation reaches, in a Git server of the tests. */
export interface StubRepository {
  id: number;
  fullName: string;
  cloneUrl: string;
}

/**
 * A stand-in for GitHub in the browser tests: the page that takes a manifest (with a link that
 * sends the browser back with a code, as Create GitHub App does), and the API calls the server makes
 * for the app, under `/api/v3` as on a GitHub Enterprise Server: installations and their tokens and
 * repositories, the files of pull requests, commit statuses, comments, and branches, files and pull
 * requests for proposed changes, which it keeps for the tests to look at. JWTs and tokens are not
 * checked here; the server's tests do that against their own stub.
 */
export class GitHubStub {
  readonly code = 'e2e-code';
  readonly privateKey = generateKeyPairSync('rsa', {
    modulusLength: 2048,
    privateKeyEncoding: { type: 'pkcs1', format: 'pem' },
    publicKeyEncoding: { type: 'spki', format: 'pem' },
  }).privateKey;
  readonly webhookSecret = 'e2e-webhook-secret';
  readonly appId = 4242;
  private server: Server | null = null;
  readonly repositories: StubRepository[] = [];
  readonly statuses: { commit: string; state: string; context: string }[] = [];
  readonly comments = new Map<number, { number: number; body: string }>();
  readonly branches: string[] = [];
  readonly fileChanges: { path: string; branch: string; content: string }[] = [];
  readonly pullRequests: { title: string; head: string; base: string }[] = [];

  readonly port: number;

  constructor(port: number) {
    this.port = port;
  }

  get web(): string {
    return `http://127.0.0.1:${this.port}`;
  }

  async start(): Promise<void> {
    this.server = createServer((request, response) => {
      void this.answer(request).then(({ status, body, location, html }) => {
        if (html !== undefined) {
          response.writeHead(status, { 'content-type': 'text/html; charset=utf-8' });
          response.end(html);
          return;
        }
        response.writeHead(status, location ? { location } : { 'content-type': 'application/json' });
        response.end(body === undefined ? undefined : JSON.stringify(body));
      });
    });
    await new Promise<void>((resolve) => this.server?.listen(this.port, '127.0.0.1', resolve));
  }

  async stop(): Promise<void> {
    await new Promise<void>((resolve) => (this.server ? this.server.close(() => resolve()) : resolve()));
  }

  private app() {
    return {
      id: this.appId,
      slug: 'autocanvas-e2e',
      name: 'AutoCanvas (e2e)',
      client_id: 'Iv1.e2e',
      owner: { login: 'acme' },
      html_url: `${this.web}/apps/autocanvas-e2e`,
    };
  }

  private async answer(
    request: IncomingMessage,
  ): Promise<{ status: number; body?: unknown; location?: string; html?: string }> {
    const url = new URL(request.url ?? '/', this.web);
    const text = await read(request);
    if (request.method === 'POST' && url.pathname.endsWith('/settings/apps/new')) {
      // The browser's form: the manifest says where to send it back.
      const manifest = JSON.parse(new URLSearchParams(text).get('manifest') ?? '{}') as { redirect_url: string };
      const back = new URL(manifest.redirect_url);
      back.searchParams.set('code', this.code);
      back.searchParams.set('state', url.searchParams.get('state') ?? '');
      // As GitHub: a page of its own, which sends the browser back once the user creates the app.
      return {
        status: 200,
        html: `<!doctype html><title>New GitHub App</title><a href="${back.toString()}">Create GitHub App for acme</a>`,
      };
    }
    if (request.method === 'POST' && url.pathname === `/api/v3/app-manifests/${this.code}/conversions`) {
      return {
        status: 201,
        body: { ...this.app(), client_secret: 'e2e-client', webhook_secret: this.webhookSecret, pem: this.privateKey },
      };
    }
    const repository = this.answerRepositories(request.method ?? 'GET', url, text);
    if (repository) {
      return repository;
    }
    if (url.pathname === '/api/v3/app') {
      return { status: 200, body: this.app() };
    }
    if (url.pathname === '/api/v3/app/installations') {
      return {
        status: 200,
        body: [
          {
            id: 7,
            account: { login: 'acme', type: 'Organization' },
            repository_selection: 'selected',
            suspended_at: null,
          },
        ],
      };
    }
    return { status: 404, body: { message: 'Not Found' } };
  }

  private repositoryJson(repository: StubRepository) {
    return {
      id: repository.id,
      full_name: repository.fullName,
      clone_url: repository.cloneUrl,
      default_branch: 'main',
      private: false,
    };
  }

  /** What an installation token does; `null` for anything else. */
  private answerRepositories(method: string, url: URL, text: string): { status: number; body?: unknown } | null {
    const path = url.pathname;
    if (method === 'POST' && /^\/api\/v3\/app\/installations\/\d+\/access_tokens$/.test(path)) {
      return { status: 201, body: { token: 'ghs_e2e', expires_at: new Date(Date.now() + 3_600_000).toISOString() } };
    }
    if (path === '/api/v3/installation/repositories') {
      return {
        status: 200,
        body: {
          total_count: this.repositories.length,
          repositories: this.repositories.map((r) => this.repositoryJson(r)),
        },
      };
    }
    const match = /^\/api\/v3\/repos\/([^/]+)\/([^/]+)(\/.*)?$/.exec(path);
    const found = match ? this.repositories.find((r) => r.fullName === `${match[1]}/${match[2]}`) : undefined;
    if (!match || !found) {
      return null;
    }
    const rest = match[3] ?? '';
    const body = text ? (JSON.parse(text) as Record<string, string>) : {};
    if (rest === '') {
      return { status: 200, body: this.repositoryJson(found) };
    }
    if (/^\/pulls\/\d+\/files$/.test(rest)) {
      return { status: 200, body: [] };
    }
    if (method === 'POST' && rest.startsWith('/statuses/')) {
      this.statuses.push({
        commit: rest.slice('/statuses/'.length),
        state: body['state'] ?? '',
        context: body['context'] ?? '',
      });
      return { status: 201, body: {} };
    }
    const comment = /^\/issues\/(\d+)\/comments$/.exec(rest);
    if (method === 'POST' && comment) {
      const id = 100 + this.comments.size + 1;
      this.comments.set(id, { number: Number(comment[1]), body: body['body'] ?? '' });
      return { status: 201, body: { id } };
    }
    const update = /^\/issues\/comments\/(\d+)$/.exec(rest);
    if (method === 'PATCH' && update) {
      const existing = this.comments.get(Number(update[1]));
      if (!existing) {
        return { status: 404, body: { message: 'Not Found' } };
      }
      this.comments.set(Number(update[1]), { ...existing, body: body['body'] ?? '' });
      return { status: 200, body: {} };
    }
    if (rest.startsWith('/contents/')) {
      const file = decodeURIComponent(rest.slice('/contents/'.length));
      if (method === 'GET') {
        return { status: 200, body: { sha: `blob-${file}` } };
      }
      this.fileChanges.push({
        path: file,
        branch: body['branch'] ?? '',
        content: Buffer.from(body['content'] ?? '', 'base64').toString('utf8'),
      });
      return { status: 200, body: {} };
    }
    if (method === 'POST' && rest === '/git/refs') {
      this.branches.push((body['ref'] ?? '').replace('refs/heads/', ''));
      return { status: 201, body: {} };
    }
    if (method === 'POST' && rest === '/pulls') {
      this.pullRequests.push({ title: body['title'] ?? '', head: body['head'] ?? '', base: body['base'] ?? '' });
      const number = 40 + this.pullRequests.length;
      return { status: 201, body: { number, html_url: `${this.web}/${found.fullName}/pull/${number}` } };
    }
    return null;
  }
}

async function read(request: IncomingMessage): Promise<string> {
  const chunks: Buffer[] = [];
  for await (const chunk of request) {
    chunks.push(chunk as Buffer);
  }
  return Buffer.concat(chunks).toString('utf8');
}
