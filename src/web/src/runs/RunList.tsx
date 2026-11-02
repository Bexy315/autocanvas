import { Link } from 'react-router';
import type { RunSummary } from '../api/runs';
import { duration } from '../lib/time';
import { RunStateBadge } from '../ui/Badge';
import { RelativeTime } from '../ui/RelativeTime';
import { startedText } from './runText';
import { useNow } from './useNow';

/** Runs, newest first, each leading to its page; with the automation's name where runs of several are listed. */
export function RunList({
  runs,
  label,
  showAutomation = false,
}: {
  runs: readonly RunSummary[];
  label: string;
  showAutomation?: boolean;
}) {
  const now = useNow(runs.some((run) => run.state === 'Running'));
  return (
    <ul aria-label={label} className="divide-y divide-line">
      {runs.map((run) => (
        <li key={run.id}>
          <Link
            to={`/runs/${run.id}`}
            aria-label={`${showAutomation ? `${run.automationName} ` : ''}run ${run.number}, ${run.state}`}
            className="flex flex-wrap items-center gap-x-3 gap-y-1 px-4 py-2.5 hover:bg-sunken"
          >
            <span className="w-12 font-mono text-xs text-muted">#{run.number}</span>
            <RunStateBadge state={run.state} />
            {showAutomation && <span className="truncate font-medium">{run.automationName}</span>}
            <span className="min-w-0 flex-1 truncate text-sm text-muted">{startedText(run)}</span>
            <span className="text-xs text-muted">
              <RelativeTime value={run.createdAt} /> · {duration(run.createdAt, run.finishedAt, now)}
            </span>
          </Link>
        </li>
      ))}
    </ul>
  );
}
