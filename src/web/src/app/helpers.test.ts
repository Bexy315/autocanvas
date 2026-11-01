import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../api/problem';
import { shouldRetry } from './queryClient';
import { returnQuery, safeReturnTo } from './returnTo';
import { readSetting, writeSetting } from './storage';
import { shortVersion } from './version';

describe('where to go after signing in', () => {
  it.each([
    ['/account', '/account'],
    ['/automations/42?tab=runs', '/automations/42?tab=runs'],
    [null, '/'],
    ['', '/'],
    ['https://evil.example/', '/'],
    ['//evil.example/', '/'],
    ['/\\evil.example', '/'],
    ['/sign-in', '/'],
    ['/sign-in?returnTo=%2F', '/'],
    ['/change-password', '/'],
  ])('%s goes to %s', (value, expected) => {
    expect(safeReturnTo(value)).toBe(expected);
  });

  it('keeps the way back in the query, with extra parameters', () => {
    expect(returnQuery('/account', { expired: '1' })).toBe('?expired=1&returnTo=%2Faccount');
    expect(returnQuery('/')).toBe('');
  });
});

describe('versions', () => {
  it.each([
    ['1.0.0+9dad45aec10d5f2c', '1.0.0 (9dad45a)'],
    ['1.0.0', '1.0.0'],
  ])('%s is shown as %s', (version, shown) => {
    expect(shortVersion(version)).toBe(shown);
  });
});

describe('settings', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('are remembered', () => {
    writeSetting('autocanvas.test', 'yes');
    expect(readSetting('autocanvas.test')).toBe('yes');
  });

  it('fall back to nothing when storage is blocked', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('Blocked', 'SecurityError');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('Blocked', 'SecurityError');
    });

    expect(() => writeSetting('autocanvas.test', 'yes')).not.toThrow();
    expect(readSetting('autocanvas.test')).toBeNull();
  });
});

describe('retrying failed requests', () => {
  it.each([
    ['an unreachable server', new ApiError(502, {}), 4, true],
    ['an unreachable server for long', new ApiError(502, {}), 5, false],
    ['a lost connection', new TypeError('Failed to fetch'), 4, true],
    ['a server error', new ApiError(500, {}), 1, true],
    ['a repeated server error', new ApiError(500, {}), 2, false],
    ['a refusal', new ApiError(403, {}), 0, false],
  ])('%s: retried or not', (_, error, failures, retried) => {
    expect(shouldRetry(failures, error)).toBe(retried);
  });
});
