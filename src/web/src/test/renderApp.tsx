import { render } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from '../app/App';
import { createQueryClient } from '../app/queryClient';
import { routes } from '../app/routes';

/** The whole app at `path`, as a browser would open it, with a fresh cache. */
export function renderApp(path: string) {
  globalThis.history.pushState({}, '', path);
  const client = createQueryClient();
  client.setDefaultOptions({ queries: { retry: false } });
  const user = userEvent.setup();
  return { user, client, ...render(<App routes={routes} client={client} />) };
}
