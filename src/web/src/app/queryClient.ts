import { QueryClient } from '@tanstack/react-query';
import { ApiError } from '../api/problem';

/** Problems the server decided are not retried; network failures and 5xx are, a few times. */
export function shouldRetry(failures: number, error: unknown): boolean {
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
