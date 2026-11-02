import { History } from 'lucide-react';
import { useState } from 'react';
import { useRecentRuns, type RunState } from '../api/runs';
import { usePageInfo } from '../layout/pageContext';
import { RunList } from '../runs/RunList';
import { ErrorAlert } from '../ui/Alert';
import { Button } from '../ui/Button';
import { EmptyState } from '../ui/EmptyState';
import { PageHeader } from '../ui/PageHeader';
import { Loading } from '../ui/Spinner';

const states: readonly { value: RunState | null; label: string }[] = [
  { value: null, label: 'All' },
  { value: 'Running', label: 'Running' },
  { value: 'Failed', label: 'Failed' },
  { value: 'Succeeded', label: 'Succeeded' },
  { value: 'Cancelled', label: 'Cancelled' },
];

/** The newest runs of every automation the user may see. */
export function RunsPage() {
  const [state, setState] = useState<RunState | null>(null);
  const runs = useRecentRuns(state);
  usePageInfo({ crumbs: [{ label: 'Runs', to: null }] });
  const items = runs.data?.pages.flatMap((page) => page.items) ?? [];

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-5 p-6">
      <PageHeader title="Runs">The newest runs of every automation you may see.</PageHeader>
      <fieldset className="flex flex-wrap gap-1">
        <legend className="sr-only">Show runs that are</legend>
        {states.map((entry) => (
          <button
            key={entry.label}
            type="button"
            aria-pressed={state === entry.value}
            onClick={() => setState(entry.value)}
            className="rounded-control border border-line-strong px-2.5 py-1 text-sm text-muted hover:text-ink aria-pressed:border-accent aria-pressed:bg-accent-soft aria-pressed:text-accent"
          >
            {entry.label}
          </button>
        ))}
      </fieldset>
      {runs.isPending && <Loading label="Loading runs…" />}
      {runs.isError && <ErrorAlert error={runs.error} title="The runs could not be loaded" />}
      {runs.data && items.length === 0 && (
        <EmptyState Icon={History} title={state ? `No runs that are ${state.toLowerCase()}` : 'No runs yet'}>
          Runs appear here when automations are started, by hand or by their triggers.
        </EmptyState>
      )}
      {items.length > 0 && (
        <div className="rounded-panel border border-line bg-surface">
          <RunList runs={items} label="Runs" showAutomation />
        </div>
      )}
      {runs.hasNextPage && (
        <Button className="self-center" busy={runs.isFetchingNextPage} onClick={() => void runs.fetchNextPage()}>
          Load older runs
        </Button>
      )}
    </div>
  );
}
