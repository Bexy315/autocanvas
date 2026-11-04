import { execFileSync, spawn } from 'node:child_process';
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { createServer, type IncomingMessage, type Server, type ServerResponse } from 'node:http';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';

/**
 * A Git server for the browser tests: bare repositories in a temporary directory, served over http
 * by `git http-backend` as a CGI program, as the server's own tests do in C#. The real server
 * connects repositories from it like from any Generic Git host.
 */
export class GitServer {
  private readonly root = mkdtempSync(join(tmpdir(), 'autocanvas-e2e-git-'));
  private server: Server | null = null;

  readonly port: number;

  constructor(port: number) {
    this.port = port;
  }

  /** The clone address of a repository. */
  url(repository: string): string {
    return `http://127.0.0.1:${this.port}/${repository}.git`;
  }

  async start(): Promise<void> {
    this.server = createServer((request, response) => void this.serve(request, response));
    await new Promise<void>((resolve) => this.server?.listen(this.port, '127.0.0.1', resolve));
  }

  async stop(): Promise<void> {
    await new Promise<void>((resolve) => (this.server ? this.server.close(() => resolve()) : resolve()));
    rmSync(this.root, { recursive: true, force: true });
  }

  /** An empty repository whose default branch is `main`. */
  create(repository: string): void {
    git(this.root, 'init', '--bare', '--quiet', '--initial-branch=main', join(this.root, `${repository}.git`));
  }

  /** Commits files to `main` (a null content deletes one) and returns the commit. */
  commit(repository: string, files: Record<string, string | null>, message: string): string {
    const work = mkdtempSync(join(tmpdir(), 'autocanvas-e2e-work-'));
    try {
      const bare = join(this.root, `${repository}.git`);
      git(work, 'init', '--quiet', '--initial-branch=main', work);
      if (git(work, '--git-dir', bare, 'branch', '--list').trim().length > 0) {
        git(work, 'fetch', '--quiet', bare, 'main');
        git(work, 'reset', '--quiet', '--hard', 'FETCH_HEAD');
      }
      for (const [path, content] of Object.entries(files)) {
        const file = join(work, path);
        if (content === null) {
          rmSync(file, { force: true });
        } else {
          mkdirSync(dirname(file), { recursive: true });
          writeFileSync(file, content);
        }
      }
      git(work, 'add', '--all');
      git(
        work,
        '-c',
        'user.name=Ada Lovelace',
        '-c',
        'user.email=ada@example.org',
        'commit',
        '--quiet',
        '--allow-empty',
        '-m',
        message,
      );
      git(work, 'push', '--quiet', bare, 'HEAD:refs/heads/main');
      return git(work, 'rev-parse', 'HEAD').trim();
    } finally {
      rmSync(work, { recursive: true, force: true });
    }
  }

  private async serve(request: IncomingMessage, response: ServerResponse): Promise<void> {
    const chunks: Buffer[] = [];
    for await (const chunk of request) {
      chunks.push(chunk as Buffer);
    }
    const body = Buffer.concat(chunks);
    const url = new URL(request.url ?? '/', `http://127.0.0.1:${this.port}`);
    const backend = spawn('git', ['http-backend'], {
      env: {
        ...process.env,
        GIT_PROJECT_ROOT: this.root,
        GIT_HTTP_EXPORT_ALL: '1',
        PATH_INFO: url.pathname,
        QUERY_STRING: url.search.slice(1),
        REQUEST_METHOD: request.method ?? 'GET',
        CONTENT_TYPE: request.headers['content-type'] ?? '',
        CONTENT_LENGTH: String(body.length),
        REMOTE_ADDR: '127.0.0.1',
        HTTP_CONTENT_ENCODING: request.headers['content-encoding'] ?? '',
        GIT_PROTOCOL: String(request.headers['git-protocol'] ?? ''),
      },
    });
    backend.stdin.end(body);
    const output: Buffer[] = [];
    for await (const chunk of backend.stdout) {
      output.push(chunk as Buffer);
    }
    const all = Buffer.concat(output);

    // The CGI answer: headers up to an empty line, then the body.
    let split = all.indexOf('\r\n\r\n');
    let separator = 4;
    if (split < 0) {
      split = all.indexOf('\n\n');
      separator = 2;
    }
    let status = 200;
    const headers: Record<string, string> = {};
    for (const line of all.subarray(0, Math.max(split, 0)).toString('latin1').split(/\r?\n/)) {
      const colon = line.indexOf(':');
      if (colon < 0) {
        continue;
      }
      const name = line.slice(0, colon).trim();
      const value = line.slice(colon + 1).trim();
      if (name.toLowerCase() === 'status') {
        status = Number(value.slice(0, 3));
      } else {
        headers[name] = value;
      }
    }
    response.writeHead(status, headers);
    response.end(split < 0 ? all : all.subarray(split + separator));
  }
}

function git(directory: string, ...args: string[]): string {
  return execFileSync('git', args, { cwd: directory, encoding: 'utf8' });
}
