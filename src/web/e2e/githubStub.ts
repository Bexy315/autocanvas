import { generateKeyPairSync } from 'node:crypto';
import { createServer, type IncomingMessage, type Server } from 'node:http';

/**
 * A stand-in for GitHub in the browser tests: the page that takes a manifest (with a link that
 * sends the browser back with a code, as Create GitHub App does), and the API calls the server makes
 * for the app, under `/api/v3` as on a GitHub Enterprise Server. JWTs are not checked here; the
 * server's tests do that against their own stub.
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
}

async function read(request: IncomingMessage): Promise<string> {
  const chunks: Buffer[] = [];
  for await (const chunk of request) {
    chunks.push(chunk as Buffer);
  }
  return Buffer.concat(chunks).toString('utf8');
}
