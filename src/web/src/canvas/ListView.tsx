import { clsx } from 'clsx';
import { triggerId, type Graph } from './model';

/**
 * The canvas as a list: the same structure in reading order, for screen readers, keyboard users and
 * small screens. Choosing a job shows it in the file, like a card does.
 */
export function ListView({
  graph,
  selectedJob,
  onSelectJob,
}: {
  graph: Graph;
  selectedJob?: string | null;
  onSelectJob?: (id: string) => void;
}) {
  const waitsFor = (id: string) =>
    graph.edges
      .filter((edge) => edge.target === id && edge.source !== triggerId && edge.kind !== 'finally')
      .map((edge) => (edge.kind === 'success' ? edge.source : `${edge.source} (${edge.label ?? edge.kind})`));

  return (
    <div className="h-full overflow-auto p-3">
      <section aria-labelledby="list-trigger" className="mb-3">
        <h3 id="list-trigger" className="font-semibold">
          Trigger
        </h3>
        <p className="text-muted">{graph.triggers.length > 0 ? graph.triggers.join(', ') : 'Nothing starts it yet.'}</p>
      </section>
      <ol aria-label="Jobs" className="flex flex-col gap-2">
        {graph.jobs.map((job) => {
          const needs = waitsFor(job.id);
          return (
            <li key={job.id}>
              <button
                type="button"
                onClick={() => onSelectJob?.(job.id)}
                aria-current={job.id === selectedJob ? 'true' : undefined}
                className={clsx(
                  'flex w-full flex-col gap-0.5 rounded-control border px-3 py-2 text-left',
                  job.id === selectedJob ? 'border-accent bg-accent-soft' : 'border-line hover:bg-sunken',
                )}
              >
                <span className="font-medium">
                  {job.title}
                  <span className="ml-2 text-xs font-normal text-muted">
                    {job.kind === 'switch'
                      ? 'switch'
                      : job.kind === 'join'
                        ? 'join'
                        : job.isFinally
                          ? 'finally'
                          : 'job'}
                  </span>
                </span>
                <span className="text-xs text-muted">
                  {job.isFinally
                    ? 'Runs last.'
                    : needs.length > 0
                      ? `Needs ${needs.join(', ')}.`
                      : 'Starts with the run.'}
                  {job.kind === 'step' && ` ${job.steps.length === 1 ? '1 step' : `${job.steps.length} steps`}.`}
                  {job.kind === 'switch' &&
                    ` Cases: ${job.cases.map((entry) => `${entry.name} (${entry.label})`).join(', ')}.`}
                </span>
                {job.problems.map((problem) => (
                  <span key={problem} className="text-xs text-danger">
                    {problem}
                  </span>
                ))}
              </button>
            </li>
          );
        })}
      </ol>
    </div>
  );
}
