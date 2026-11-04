import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { useRecentRuns, useTriggers, type AutomationTriggers, type AutomationView } from '../../api/automations';
import type { Diagnostic } from '../../api/problem';
import { hasRole } from '../../lib/roles';
import { ErrorAlert } from '../../ui/Alert';
import { RunStateBadge } from '../../ui/Badge';
import { DiagnosticList } from '../../ui/DiagnosticList';
import { Section } from '../../ui/PageHeader';
import { RelativeTime } from '../../ui/RelativeTime';
import { Loading } from '../../ui/Spinner';
import { YamlView } from '../../ui/YamlView';
import { useAutomationContext } from './context';

const triggerNames: Record<string, string> = {
  Manual: 'By hand',
  Schedule: 'Schedule',
  Webhook: 'Webhook',
  AutomationCompleted: 'After another automation',
  Push: 'Push',
  PullRequest: 'Pull request',
};

/** What an automation is: what starts it, how its last runs went, and its file. */
export function OverviewTab() {
  const { automation } = useAutomationContext();
  const warnings = automation.warnings as Diagnostic[];

  return (
    <div className="flex flex-col gap-5">
      {warnings.length > 0 && (
        <Section title="Worth a look">
          <div className="p-3">
            <DiagnosticList diagnostics={warnings} label="Warnings" />
          </div>
        </Section>
      )}
      {automation.repository && <RepositorySourceCard source={automation.repository} />}
      <div className="grid gap-5 md:grid-cols-2">
        <Triggers id={automation.id} />
        <RecentRuns id={automation.id} />
      </div>
      <Section
        title="File"
        actions={
          <Link to={`/automations/${automation.id}/edit`} className="text-sm font-medium text-accent hover:underline">
            {hasRole(automation.myRole, 'Editor') && (automation.repository?.proposesChanges ?? true)
              ? 'Edit'
              : 'Open in editor'}
          </Link>
        }
      >
        <YamlView text={automation.yaml} label={`File of ${automation.title}`} />
      </Section>
    </div>
  );
}

/** Where a repository automation comes from, and that it is changed there. */
function RepositorySourceCard({ source }: { source: NonNullable<AutomationView['repository']> }) {
  const commit = source.commit?.slice(0, 7);
  return (
    <Section title="From repository">
      <div className="flex flex-col gap-2 p-3 text-sm">
        <dl className="grid grid-cols-[max-content_1fr] gap-x-4 gap-y-1">
          <dt className="text-muted">Repository</dt>
          <dd>
            {source.webUrl ? (
              <a href={source.webUrl} target="_blank" rel="noreferrer" className="hover:underline">
                {source.repositoryName}
              </a>
            ) : (
              source.repositoryName
            )}
          </dd>
          <dt className="text-muted">File</dt>
          <dd className="font-mono text-xs leading-5">
            {source.fileUrl ? (
              <a href={source.fileUrl} target="_blank" rel="noreferrer" className="hover:underline">
                {source.path}
              </a>
            ) : (
              source.path
            )}
          </dd>
          {commit && (
            <>
              <dt className="text-muted">Commit</dt>
              <dd className="font-mono text-xs leading-5">
                {source.commitUrl ? (
                  <a href={source.commitUrl} target="_blank" rel="noreferrer" className="hover:underline">
                    {commit}
                  </a>
                ) : (
                  commit
                )}
              </dd>
            </>
          )}
        </dl>
        <p className="text-muted">
          {source.proposesChanges
            ? 'Changed in the repository: edits here become a pull request, and the automation follows once it is merged.'
            : 'Changed in the repository: AutoCanvas follows its pushes, and it cannot be edited here.'}
        </p>
      </div>
    </Section>
  );
}

function Triggers({ id }: { id: string }) {
  const triggers = useTriggers(id);
  return (
    <Section title="Started by">
      {triggers.isPending && <Loading />}
      {triggers.isError && (
        <div className="p-3">
          <ErrorAlert error={triggers.error} />
        </div>
      )}
      {triggers.data && <TriggerList triggers={triggers.data} />}
    </Section>
  );
}

function TriggerList({ triggers }: { triggers: AutomationTriggers }) {
  const entries: { title: string; detail?: ReactNode }[] = [
    ...(triggers.manual ? [{ title: 'By hand', detail: 'People with the Operator role start it.' }] : []),
    ...(triggers.webhook ? [{ title: 'Webhook', detail: 'Another system calls its address.' }] : []),
    ...triggers.schedules.map((schedule) => ({
      title: `Schedule: ${schedule.schedule}${schedule.timeZone ? ` (${schedule.timeZone})` : ''}`,
      detail: schedule.nextAt ? (
        <>
          Next <RelativeTime value={schedule.nextAt} />
        </>
      ) : (
        'No next time'
      ),
    })),
    ...(triggers.follows
      ? [
          {
            title: `After ${triggers.follows.automation ?? 'another automation'}`,
            detail: `When its run ends: ${triggers.follows.on}`,
          },
        ]
      : []),
  ];

  if (entries.length === 0) {
    return <p className="px-4 py-3 text-muted">Nothing starts it yet. Add a trigger to the file.</p>;
  }
  return (
    <ul className="divide-y divide-line">
      {entries.map((entry) => (
        <li key={entry.title} className="px-4 py-2.5">
          <p className="font-medium">{entry.title}</p>
          {entry.detail && <p className="text-xs text-muted">{entry.detail}</p>}
        </li>
      ))}
    </ul>
  );
}

function RecentRuns({ id }: { id: string }) {
  const runs = useRecentRuns(id);
  return (
    <Section title="Latest runs">
      {runs.isPending && <Loading />}
      {runs.isError && (
        <div className="p-3">
          <ErrorAlert error={runs.error} />
        </div>
      )}
      {runs.data?.items.length === 0 && <p className="px-4 py-3 text-muted">It has not run yet.</p>}
      {runs.data && runs.data.items.length > 0 && (
        <ul className="divide-y divide-line">
          {runs.data.items.map((run) => (
            <li key={run.id}>
              <Link to={`/runs/${run.id}`} className="flex items-center gap-3 px-4 py-2.5 hover:bg-sunken">
                <span className="w-10 font-mono text-xs text-muted">#{run.number}</span>
                <RunStateBadge state={run.state} />
                <span className="min-w-0 flex-1 truncate text-muted">
                  {triggerNames[run.trigger] ?? run.trigger}, {run.triggeredByName}
                </span>
                <span className="text-xs text-muted">
                  <RelativeTime value={run.createdAt} />
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </Section>
  );
}
