/**
 * A test's history in the automation's runs: its newest results as a strip (oldest left), its
 * durations, its failures by message, and a hint when it looks flaky.
 */
import { clsx } from 'clsx';
import { TriangleAlert, X } from 'lucide-react';
import { Link } from 'react-router';
import { useTestHistory, type TestCase, type TestHistoryEntry } from '../../api/reports';
import { ErrorAlert } from '../../ui/Alert';
import { Loading } from '../../ui/Spinner';
import { failureGroups, flakiness, median, testDuration } from './reportModel';

const tones: Record<TestHistoryEntry['outcome'], string> = {
  Passed: 'bg-ok',
  Failed: 'bg-danger',
  Skipped: 'bg-line-strong',
};

export function TestHistoryPanel({
  automationId,
  test,
  runId,
  onClose,
}: {
  automationId: string;
  test: TestCase;
  /** The run being looked at, marked in the strip. */
  runId: string;
  onClose: () => void;
}) {
  const history = useTestHistory(automationId, test.fullName);
  const entries = (history.data?.entries ?? []).toSorted((a, b) => Date.parse(a.reportedAt) - Date.parse(b.reportedAt));
  const flaky = flakiness(entries);
  const durations = entries.map((entry) => Number(entry.durationMs));
  const longest = Math.max(1, ...durations);

  return (
    // Esc anywhere in the panel closes it; the aside is the region the key belongs to.
    // oxlint-disable-next-line jsx-a11y/no-noninteractive-element-interactions -- see above
    <aside
      aria-label={`History of ${test.fullName}`}
      onKeyDown={(event) => {
        if (event.key === 'Escape') {
          event.preventDefault();
          onClose();
        }
      }}
      className="flex h-full w-[26rem] max-w-full shrink-0 flex-col border-l border-line bg-surface"
    >
      <header className="flex items-start gap-2 border-b border-line px-4 py-3">
        <div className="min-w-0 flex-1">
          <h2 className="truncate text-sm font-semibold">{test.name}</h2>
          <p className="truncate text-xs text-muted">{test.suite}</p>
        </div>
        <button
          type="button"
          aria-label="Close the history (Esc)"
          title="Close the history (Esc)"
          // oxlint-disable-next-line jsx-a11y/no-autofocus -- the panel opens on request; its close button takes the focus
          autoFocus
          onClick={onClose}
          className="rounded-control p-1.5 text-muted hover:bg-sunken hover:text-ink"
        >
          <X aria-hidden="true" size={14} />
        </button>
      </header>
      <div className="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto p-4">
        {history.isPending && <Loading label="Loading the history…" />}
        {history.isError && <ErrorAlert error={history.error} title="The history could not be loaded" />}
        {history.data && (
          <>
            {flaky.flaky && (
              <p className="flex gap-2 rounded-control bg-warn-soft px-3 py-2 text-sm text-ink">
                <TriangleAlert aria-hidden="true" size={16} className="mt-0.5 shrink-0 text-warn" />
                <span>
                  <strong>Looks flaky.</strong> {flaky.reason}
                </span>
              </p>
            )}
            <p className="text-sm">
              <span className="text-ok">{history.data.passed} passed</span> ·{' '}
              <span className="text-danger">{history.data.failed} failed</span> ·{' '}
              <span className="text-muted">{history.data.skipped} skipped</span>
              <span className="text-muted"> in the last {entries.length} results</span>
            </p>
            <section aria-label="Results, oldest first" className="flex flex-col gap-1.5">
              <h3 className="text-xs font-medium text-muted">Results, oldest first</h3>
              <ol className="flex flex-wrap gap-1">
                {entries.map((entry) => (
                  <li key={`${entry.reportId}-${entry.attempt}`}>
                    <Link
                      to={`/runs/${entry.runId}?view=tests`}
                      aria-label={`Run #${entry.runNumber}${Number(entry.attempt) > 1 ? `, attempt ${entry.attempt}` : ''}: ${entry.outcome.toLowerCase()}`}
                      title={`Run #${entry.runNumber}: ${entry.outcome}${entry.message ? ` — ${entry.message}` : ''}`}
                      className={clsx(
                        'block size-4 rounded-sm',
                        tones[entry.outcome],
                        entry.runId === runId && 'ring-2 ring-accent ring-offset-1 ring-offset-surface',
                      )}
                    />
                  </li>
                ))}
              </ol>
            </section>
            {entries.length > 1 && (
              <section aria-label="Durations" className="flex flex-col gap-1.5">
                <h3 className="text-xs font-medium text-muted">
                  Durations: median {testDuration(median(durations))}, longest {testDuration(longest)}
                </h3>
                <div aria-hidden="true" className="flex h-12 items-end gap-px">
                  {entries.map((entry) => (
                    <span
                      key={`${entry.reportId}-${entry.attempt}`}
                      style={{ height: `${Math.max(4, (Number(entry.durationMs) / longest) * 100)}%` }}
                      className={clsx(
                        'flex-1 rounded-t-sm',
                        entry.outcome === 'Failed' ? 'bg-danger' : 'bg-line-strong',
                      )}
                    />
                  ))}
                </div>
              </section>
            )}
            {failureGroups(entries).length > 0 && (
              <section aria-label="Failures" className="flex flex-col gap-1.5">
                <h3 className="text-xs font-medium text-muted">Failures by message</h3>
                <ul className="flex flex-col gap-1 text-sm">
                  {failureGroups(entries).map((group) => (
                    <li key={group.message} className="flex gap-2">
                      <span className="shrink-0 font-medium text-danger tabular-nums">{group.count}×</span>
                      <span className="break-words">{group.message}</span>
                    </li>
                  ))}
                </ul>
              </section>
            )}
          </>
        )}
      </div>
    </aside>
  );
}
