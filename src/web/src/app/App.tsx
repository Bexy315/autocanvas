import { QueryClientProvider, type QueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { createBrowserRouter, type RouteObject } from 'react-router';
import { RouterProvider } from 'react-router/dom';
import { ErrorBoundary } from '../ui/ErrorBoundary';
import { createQueryClient } from './queryClient';
import { ThemeProvider } from './theme';

export function App({ routes, client }: { routes: RouteObject[]; client?: QueryClient }) {
  const [queryClient] = useState(() => client ?? createQueryClient());
  const [router] = useState(() => createBrowserRouter(routes));
  return (
    <ErrorBoundary>
      <ThemeProvider>
        <QueryClientProvider client={queryClient}>
          <RouterProvider router={router} />
        </QueryClientProvider>
      </ThemeProvider>
    </ErrorBoundary>
  );
}
