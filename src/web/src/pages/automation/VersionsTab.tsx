import { clsx } from 'clsx';
import { History, RotateCcw } from 'lucide-react';
import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { useSaveAutomation, useVersion, useVersions, type VersionSummary } from '../../api/automations';
import { ConfirmDialog } from '../../dialogs/ConfirmDialog';
import { collapse, lineDiff, withoutLayout } from '../../lib/diff';
import { hasRole } from '../../lib/roles';
import { Alert, ErrorAlert } from '../../ui/Alert';
import { Button } from '../../ui/Button';
import { DiffView } from '../../ui/DiffView';
import { Section } from '../../ui/PageHeader';
import { RelativeTime } from '../../ui/RelativeTime';
import { Loading } from '../../ui/Spinner';
import { YamlView } from '../../ui/YamlView';
import { useAutomationContext } from './context';

/** Every saved version, and what changed in the one selected. */
export function VersionsTab() {
  const { automation } = useAutomationContext();
  const { number } = useParams();
  const selected = Number(number ?? automation.currentVersion);
  const versions = useVersions(automation.id);
  const list = versions.data?.pages.flatMap((page) => page.items) ?? [];

  return (
    <div className="grid gap-5 md:grid-cols-[16rem_1fr]">
      <Section title="Versions">
        {versions.isPending && <Loading />}
        {versions.isError && (
          <div className="p-3">
            <ErrorAlert error={versions.error} />
          </div>
        )}
        <ol aria-label="Versions" className="flex flex-col divide-y divide-line">
          {list.map((version) => (
            <VersionItem
              key={version.number}
              version={version}
              selected={Number(version.number) === selected}
              current={Number(version.number) === Number(automation.currentVersion)}
              to={`/automations/${automation.id}/versions/${version.number}`}
            />
          ))}
        </ol>
        {versions.hasNextPage && (
          <div className="p-2">
            <Button variant="ghost" busy={versions.isFetchingNextPage} onClick={() => void versions.fetchNextPage()}>
              Show older versions
            </Button>
          </div>
        )}
      </Section>
      <VersionDetail key={selected} number={selected} />
    </div>
  );
}

function VersionItem({
  version,
  selected,
  current,
  to,
}: {
  version: VersionSummary;
  selected: boolean;
  current: boolean;
  to: string;
}) {
  return (
    <li>
      <Link
        to={to}
        aria-current={selected ? 'page' : undefined}
        className={clsx('flex flex-col gap-0.5 px-4 py-2.5', selected ? 'bg-accent-soft' : 'hover:bg-sunken')}
      >
        <span className="flex items-center gap-2">
          <span className={clsx('font-medium', selected && 'text-accent')}>Version {version.number}</span>
          {current && <span className="text-xs text-muted">current</span>}
        </span>
        <span className="truncate text-muted">{version.message ?? 'No message'}</span>
        <span className="text-xs text-muted">
          {version.createdByName}, <RelativeTime value={version.createdAt} />
        </span>
      </Link>
    </li>
  );
}

function VersionDetail({ number }: { number: number }) {
  const { automation, tag } = useAutomationContext();
  const [view, setView] = useState<'changes' | 'file'>('changes');
  const [restoring, setRestoring] = useState(false);
  const navigate = useNavigate();
  const version = useVersion(automation.id, number);
  const previous = useVersion(automation.id, number > 1 ? number - 1 : null);
  const save = useSaveAutomation(automation.id);
  const current = number === Number(automation.currentVersion);
  const canRestore = hasRole(automation.myRole, 'Editor') && !current;

  if (version.isPending) {
    return <Loading label="Loading the version…" />;
  }
  if (version.isError) {
    return <ErrorAlert error={version.error} title="The version could not be loaded" />;
  }

  const restore = () =>
    save.mutate(
      { yaml: version.data.yaml, message: `Restore version ${number}`, tag },
      {
        onSuccess: (saved) => {
          setRestoring(false);
          void navigate(`/automations/${automation.id}/versions/${saved.currentVersion}`);
        },
      },
    );

  return (
    <Section
      title={`Version ${number}`}
      actions={
        <div className="flex items-center gap-2">
          <fieldset className="flex rounded-control border border-line-strong p-0.5">
            <legend className="sr-only">Show</legend>
            {(['changes', 'file'] as const).map((option) => (
              <button
                key={option}
                type="button"
                aria-pressed={view === option}
                onClick={() => setView(option)}
                className={clsx(
                  'h-6 rounded px-2 text-xs',
                  view === option ? 'bg-accent-soft font-medium text-accent' : 'text-muted hover:text-ink',
                )}
              >
                {option === 'changes' ? 'Changes' : 'Full file'}
              </button>
            ))}
          </fieldset>
          {canRestore && (
            <Button onClick={() => setRestoring(true)}>
              <RotateCcw aria-hidden="true" size={14} />
              Restore
            </Button>
          )}
        </div>
      }
    >
      <div className="border-b border-line px-4 py-2.5">
        <p>{version.data.message ?? 'No message'}</p>
        <p className="text-xs text-muted">
          Saved by {version.data.createdByName}, <RelativeTime value={version.data.createdAt} />
        </p>
      </div>
      {view === 'file' && <YamlView text={version.data.yaml} label={`File of version ${number}`} />}
      {view === 'changes' && number === 1 && (
        <p className="flex items-center gap-2 px-4 py-3 text-muted">
          <History aria-hidden="true" size={16} />
          The first version: there is nothing to compare it with. Choose Full file to read it.
        </p>
      )}
      {view === 'changes' && number > 1 && previous.isPending && <Loading />}
      {view === 'changes' && previous.isError && (
        <div className="p-3">
          <ErrorAlert error={previous.error} />
        </div>
      )}
      {view === 'changes' && previous.data && (
        <Changes before={previous.data.yaml} after={version.data.yaml} from={number - 1} />
      )}
      <ConfirmDialog
        title={`Restore version ${number}?`}
        open={restoring}
        onOpenChange={(open) => {
          setRestoring(open);
          save.reset();
        }}
        confirmLabel={`Restore version ${number}`}
        busy={save.isPending}
        error={save.error}
        onConfirm={restore}
      >
        Its file is saved again as a new version. The versions in between stay in the history.
      </ConfirmDialog>
    </Section>
  );
}

function Changes({ before, after, from }: { before: string; after: string; from: number }) {
  const old = withoutLayout(before);
  const now = withoutLayout(after);
  const hunks = collapse(lineDiff(old.text, now.text));

  return (
    <div className="flex flex-col gap-2 p-3">
      {(old.removed || now.removed) && (
        <Alert tone="info">Canvas positions (x-editor) are left out of this comparison.</Alert>
      )}
      {hunks.length === 0 ? (
        <p className="px-1 text-muted">
          No changes from version {from}
          {old.removed || now.removed ? ' apart from canvas positions' : ''}.
        </p>
      ) : (
        <DiffView hunks={hunks} label={`Changes from version ${from}`} />
      )}
    </div>
  );
}
