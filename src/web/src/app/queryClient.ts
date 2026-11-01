import { QueryClient } from '@tanstack/react-query';
import { ApiError, isUnreachable } from '../api/problem';

/**
 * Problems the server decided are not retried. An unreachable server (starting, restarting) is
 * tried for about half a minute with growing pauses; other server errors twice.
 */
export function shouldRetry(failures: number, error: unknown): boolean {
  if (isUnreachable(error)) {
    return failures < 5;
  }
  if (error instanceof ApiError && error.status < 500) {
    return false;
  }
  return failures < 2;
}

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: shouldRetry, refetchOnWindowFocus: true },
      mutations: { retry: false },
    },
  });
}
