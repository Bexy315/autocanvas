import { History } from 'lucide-react';
import { useTriggers } from '../../api/automations';
import { useAutomationRuns } from '../../api/runs';
import { RunList } from '../../runs/RunList';
import { ErrorAlert } from '../../ui/Alert';
import { Button } from '../../ui/Button';
import { EmptyState } from '../../ui/EmptyState';
import { Loading } from '../../ui/Spinner';
import { useAutomationContext } from './context';

/** An automation's runs, newest first; **Run** in the page's header starts one by hand. */
export function RunsTab() {
  const { automation } = useAutomationContext();
  const runs = useAutomationRuns(automation.id);
  const triggers = useTriggers(automation.id);
  const items = runs.data?.pages.flatMap((page) => page.items) ?? [];

  return (
    <div className="flex flex-col gap-3">
      <p className="text-sm text-muted">
        {triggers.data && !triggers.data.manual
          ? 'This automation is not started by hand; add `manual: true` to its triggers to allow it.'
          : 'Every run, newest first; a run shows its jobs, steps and log.'}
      </p>
      {runs.isPending && <Loading label="Loading runs…" />}
      {runs.isError && <ErrorAlert error={runs.error} title="The runs could not be loaded" />}
      {runs.data && items.length === 0 && (
        <EmptyState Icon={History} title="No runs yet">
          Runs appear here when the automation is started, by hand with Run above or by one of its triggers.
        </EmptyState>
      )}
      {items.length > 0 && (
        <div className="rounded-panel border border-line bg-surface">
          <RunList runs={items} label={`Runs of ${automation.title}`} />
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
