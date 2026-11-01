import { describe, expect, it } from 'vitest';
import { json, problem, requests, fakeServer } from '../test/fakeServer';
import { api, csrfHeader, sessionEvents, sessionExpired, unwrap } from './client';
import { ApiError } from './problem';

function countExpiries() {
  let count = 0;
  const listener = () => {
    count += 1;
  };
  sessionEvents.addEventListener(sessionExpired, listener);
  return {
    get count() {
      return count;
    },
    stop: () => sessionEvents.removeEventListener(sessionExpired, listener),
  };
}

describe('the API client', () => {
  it('sends the CSRF header on changing requests only', async () => {
    fakeServer({
      'GET /api/v1/info': () => json({ product: 'AutoCanvas', version: '1' }),
      'POST /api/v1/auth/sign-out': () => new Response(null, { status: 204 }),
    });

    await unwrap(api.GET('/api/v1/info'));
    await unwrap(api.POST('/api/v1/auth/sign-out'));

    expect(requests.map((r) => [r.method, r.headers.get(csrfHeader)])).toEqual([
      ['GET', null],
      ['POST', '1'],
    ]);
  });

  it('turns a broken rule into an error at its field', async () => {
    fakeServer({
      'PUT /api/v1/me/password': () => problem(400, 'The password must have at least 12 characters.', 'newPassword'),
    });

    const error = await unwrap(
      api.PUT('/api/v1/me/password', { body: { currentPassword: 'a', newPassword: 'b' } }),
    ).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(400);
    expect((error as ApiError).fieldErrors('newPassword')).toEqual(['The password must have at least 12 characters.']);
    expect((error as ApiError).fieldErrors('currentPassword')).toEqual([]);
  });

  it('reads the messages per property of a body that could not be read', async () => {
    fakeServer({
      'PUT /api/v1/me/password': () =>
        json(
          { status: 400, title: 'One or more validation errors occurred.', errors: { newPassword: ['Required.'] } },
          400,
        ),
    });

    const error = await unwrap(
      api.PUT('/api/v1/me/password', { body: { currentPassword: 'a', newPassword: 'b' } }),
    ).catch((e: unknown) => e);

    expect((error as ApiError).fieldErrors('newPassword')).toEqual(['Required.']);
  });

  it('reports an ended session when a request that needs one is refused', async () => {
    const expiries = countExpiries();
    fakeServer({ 'GET /api/v1/me/sessions': () => problem(401, 'Sign in first.') });

    await api.GET('/api/v1/me/sessions');

    expect(expiries.count).toBe(1);
    expiries.stop();
  });

  it('does not report an ended session for a failed sign-in or the account check', async () => {
    const expiries = countExpiries();
    fakeServer({
      'POST /api/v1/auth/sign-in': () => problem(401, 'Wrong.'),
      'GET /api/v1/me': () => problem(401, 'Sign in first.'),
    });

    await api.POST('/api/v1/auth/sign-in', { body: { userName: 'a', password: 'b' } });
    await api.GET('/api/v1/me');

    expect(expiries.count).toBe(0);
    expiries.stop();
  });
});
