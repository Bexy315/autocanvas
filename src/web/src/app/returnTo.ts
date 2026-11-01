/**
 * Where to go after signing in. Only paths of this app count: anything else (another site,
 * `//host`, the sign-in page itself) goes to the start page instead.
 */
export function safeReturnTo(value: string | null | undefined): string {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\')) {
    return '/';
  }
  if (value === '/sign-in' || value.startsWith('/sign-in?') || value.startsWith('/change-password')) {
    return '/';
  }
  return value;
}

/** A query string that brings the user back to `path` after signing in. */
export function returnQuery(path: string, extra: Record<string, string> = {}): string {
  const params = new URLSearchParams(extra);
  const target = safeReturnTo(path);
  if (target !== '/') {
    params.set('returnTo', target);
  }
  const query = params.toString();
  return query ? `?${query}` : '';
}
