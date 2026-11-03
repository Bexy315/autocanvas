import createClient, { type Middleware } from 'openapi-fetch';
import { ApiError, toProblem } from './problem';
import type { paths } from './schema';

/** The header the server requires on changing requests made with the session cookie (CSRF). */
export const csrfHeader = 'X-AutoCanvas-Request';

const readingMethods = new Set(['GET', 'HEAD', 'OPTIONS']);

/** Fires `sessionExpired` when a request that needed a session was refused with 401. */
export const sessionEvents = new EventTarget();
export const sessionExpired = 'expired';

/** Requests whose 401 is an answer ("not signed in", "wrong password", "wrong setup token"), not a session that ended. */
function answersWithoutSession(request: Request): boolean {
  const path = new URL(request.url).pathname;
  return (
    path === '/api/v1/auth/sign-in' ||
    // A wrong setup token (ADR-0020): nobody was signed in to begin with.
    path === '/api/v1/setup' ||
    (path === '/api/v1/me' && request.method === 'GET')
  );
}

export const sessionMiddleware: Middleware = {
  onRequest({ request }) {
    if (!readingMethods.has(request.method)) {
      request.headers.set(csrfHeader, '1');
    }
    return request;
  },
  onResponse({ request, response }) {
    if (response.status === 401 && !answersWithoutSession(request)) {
      sessionEvents.dispatchEvent(new Event(sessionExpired));
    }
    return response;
  },
};

/** The typed client for `/api/v1`, generated from openapi/v1.json (`npm run api`). */
export const api = createClient<paths>({
  baseUrl: globalThis.location.origin,
  credentials: 'same-origin',
});
api.use(sessionMiddleware);

type Call<T> = Promise<{ data?: T; error?: unknown; response: Response }>;

/** The data of a call, or an `ApiError` with the server's problem details. */
export async function unwrap<T>(call: Call<T>): Promise<T> {
  return (await unwrapWithTag(call)).data;
}

/** A resource with the version it was read at, to send back as `If-Match` when changing it. */
export interface Tagged<T> {
  data: T;
  /** The `ETag` header, or null when the server sent none. */
  tag: string | null;
}

/** Like `unwrap`, keeping the `ETag` (docs/technical/api.md, Concurrency). */
export async function unwrapWithTag<T>(call: Call<T>): Promise<Tagged<T>> {
  const { data, error, response } = await call;
  if (!response.ok) {
    throw new ApiError(response.status, toProblem(error, response.status));
  }
  return { data: data as T, tag: response.headers.get('ETag') };
}

/** Headers for a change based on what was read: `If-Match` with its tag, when there is one. */
export function ifMatch(tag: string | null | undefined): Record<string, string> {
  return tag ? { 'If-Match': tag } : {};
}
