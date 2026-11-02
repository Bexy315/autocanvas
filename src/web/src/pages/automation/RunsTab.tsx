import { History, Play } from 'lucide-react';
import { useNavigate } from 'react-router';
import { useTriggers } from '../../api/automations';
import { ApiError } from '../../api/problem';
import { useAutomationRuns, useStartRun } from '../../api/runs';
import { hasRole } from '../../lib/roles';
import { RunList } from '../../runs/RunList';
import { ErrorAlert } from '../../ui/Alert';
import { Button } from '../../ui/Button';
import { EmptyState } from '../../ui/EmptyState';
import { Loading } from '../../ui/Spinner';
import { useAutomationContext } from './context';

/** An automation's runs, newest first, and starting one by hand. */
export function RunsTab() {
  const { automation } = useAutomationContext();
  const runs = useAutomationRuns(automation.id);
  const triggers = useTriggers(automation.id);
  const start = useStartRun(automation.id);
  const navigate = useNavigate();
  const canStart = hasRole(automation.myRole, 'Operator') && triggers.data?.manual === true;
  const items = runs.data?.pages.flatMap((page) => page.items) ?? [];

  const run = (
    <Button
      variant="primary"
      busy={start.isPending}
      onClick={() => start.mutate(undefined, { onSuccess: (next) => void navigate(`/runs/${next.id}`) })}
    >
      <Play aria-hidden="true" size={14} />
      Run
    </Button>
  );

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center justify-between gap-3">
        <p className="text-sm text-muted">
          {triggers.data && !triggers.data.manual
            ? 'This automation is not started by hand; add `manual: true` to its triggers to allow it.'
            : 'Every run, newest first; a run shows its jobs, steps and log.'}
        </p>
        {canStart && run}
      </div>
      {start.isError && (
        <ErrorAlert
          error={start.error}
          title={
            start.error instanceof ApiError && start.error.status === 409
              ? 'It is running already, and it runs one at a time'
              : 'The run did not start'
          }
        />
      )}
      {runs.isPending && <Loading label="Loading runs…" />}
      {runs.isError && <ErrorAlert error={runs.error} title="The runs could not be loaded" />}
      {runs.data && items.length === 0 && (
        <EmptyState Icon={History} title="No runs yet" {...(canStart ? { action: run } : {})}>
          Runs appear here when the automation is started, by hand or by one of its triggers.
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
