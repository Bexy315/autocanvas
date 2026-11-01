/**
 * A stand-in for the AutoCanvas API in component tests: `fetch` (installed by setup.ts) answers
 * from the routes a test gives, keyed like `GET /api/v1/me`, and records every request.
 */
export type Handler = (request: Request) => Response | Promise<Response>;

let routes = new Map<string, Handler>();

/** Every request since the test began, in order. */
export const requests: Request[] = [];

export function fakeServer(handlers: Record<string, Handler>): void {
  routes = new Map(Object.entries(handlers));
}

export function resetFakeServer(): void {
  routes = new Map();
  requests.length = 0;
}

export async function fakeFetch(input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
  const request = input instanceof Request ? input : new Request(input, init);
  requests.push(request.clone());
  const key = `${request.method} ${new URL(request.url).pathname}`;
  const handler = routes.get(key);
  if (!handler) {
    return problem(404, `The fake server has no route for ${key}.`);
  }
  return handler(request);
}

export function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

export function problem(status: number, detail: string, errors?: Record<string, string[]>): Response {
  return new Response(JSON.stringify({ status, title: 'Problem', detail, ...(errors ? { errors } : {}) }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  });
}

export function noContent(): Response {
  return new Response(null, { status: 204 });
}

/** A user as `GET /api/v1/me` returns it. */
export function me(overrides: { mustChangePassword?: boolean; displayName?: string } = {}) {
  return {
    user: {
      id: '00000000-0000-0000-0000-000000000001',
      userName: 'ada',
      displayName: overrides.displayName ?? 'Ada Lovelace',
      email: null,
      isAdmin: true,
      isDisabled: false,
      mustChangePassword: overrides.mustChangePassword ?? false,
      createdAt: '2026-11-01T10:00:00Z',
      lastSignInAt: '2026-11-01T11:00:00Z',
    },
    groups: [{ id: '00000000-0000-0000-0000-000000000002', name: 'builders' }],
  };
}

export const info = { product: 'AutoCanvas', version: '1.0.0+9dad45aec10d5f2c2e0af3a14f6edc691a1f3300' };
