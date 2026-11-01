import { Workflow } from 'lucide-react';
import { useMe } from '../api/session';

/** The start page. Folders and automations arrive with S3 step 3; until then it says so. */
export function AutomationsPage() {
  const me = useMe();
  const name = me.data?.user.displayName;

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-6 p-6">
      <h1 className="text-xl font-semibold">Automations</h1>
      <div className="canvas-grid flex flex-col items-center gap-3 rounded-panel border border-dashed border-line-strong px-6 py-16 text-center">
        <span className="flex size-10 items-center justify-center rounded-panel bg-accent-soft text-accent">
          <Workflow aria-hidden="true" size={20} />
        </span>
        <h2 className="text-base font-semibold">{name ? `Welcome, ${name}` : 'Welcome'}</h2>
        <p className="max-w-md text-muted">
          Folders and automations will be listed here. Until the web UI can show them, they are managed through the HTTP
          API.
        </p>
      </div>
    </div>
  );
}
