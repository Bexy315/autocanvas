import { Component, type ErrorInfo, type ReactNode } from 'react';
import { Button } from './Button';

interface State {
  error: Error | null;
}

/** Catches what a page throws while rendering, so one broken view does not blank the whole app. */
export class ErrorBoundary extends Component<{ children: ReactNode }, State> {
  override state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  override componentDidCatch(error: Error, info: ErrorInfo): void {
    console.error('The page failed to render.', error, info.componentStack);
  }

  override render() {
    if (this.state.error) {
      return <ErrorPage message={this.state.error.message} />;
    }
    return this.props.children;
  }
}

/** A page for errors the UI cannot recover from by itself. */
export function ErrorPage({ message }: { message: string }) {
  return (
    <div role="alert" className="mx-auto flex max-w-md flex-col items-start gap-3 p-8">
      <h1 className="text-lg font-semibold">This page stopped working</h1>
      <p className="text-muted">Reloading usually helps. If it happens again, the details below say what failed.</p>
      <pre className="w-full overflow-auto rounded-control bg-sunken p-2 font-mono text-xs">{message}</pre>
      <Button variant="primary" onClick={() => globalThis.location.reload()}>
        Reload
      </Button>
    </div>
  );
}
