/**
 * Where each value a folder or a run of an automation reads comes from: for every name, the
 * definition that wins and, unfolded, every definition from the instance down, those overridden
 * struck through. Variable values for those who may see them; secret values never.
 */
import { clsx } from 'clsx';
import { ChevronDown, ChevronRight, Search } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';
import { useConfiguration, type ConfigurationEntry, type ConfigurationSource, type Scope } from '../api/configuration';
import { ErrorAlert } from '../ui/Alert';
import { Loading } from '../ui/Spinner';

/** Where a definition is, in words: `the instance`, `folder Team`, `this automation`. */
function sourceText(source: ConfigurationSource, secret: boolean): string {
  switch (source.kind) {
    case 'Instance':
      return 'the instance';
    case 'Folder':
      return `folder ${source.folderName ?? ''}`.trim();
    default:
      return secret ? 'this automation' : "the automation's file";
  }
}

function SourceLabel({ source, secret }: { source: ConfigurationSource; secret: boolean }) {
  const text = sourceText(source, secret);
  return source.kind === 'Folder' && source.folderId ? (
    <Link to={`/folders/${source.folderId}/configuration`} className="hover:underline">
      {text}
    </Link>
  ) : (
    <span>{text}</span>
  );
}

function Entry({ entry, secret }: { entry: ConfigurationEntry; secret: boolean }) {
  const [open, setOpen] = useState(false);
  const overridden = entry.definitions.length > 1;
  return (
    <li>
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1 px-4 py-2">
        <button
          type="button"
          aria-expanded={open}
          disabled={!overridden}
          onClick={() => setOpen(!open)}
          aria-label={overridden ? `Every definition of ${entry.name}` : undefined}
          className="text-muted disabled:invisible"
        >
          {open ? <ChevronDown aria-hidden="true" size={14} /> : <ChevronRight aria-hidden="true" size={14} />}
        </button>
        <span className="w-48 truncate font-mono text-sm">{entry.name}</span>
        <span className="min-w-0 flex-1 truncate font-mono text-sm">
          {secret ? (
            <span className="text-muted">••••••••</span>
          ) : entry.winner.value === null || entry.winner.value === undefined ? (
            <span className="font-sans text-muted">Hidden: values need the Operator role</span>
          ) : (
            entry.winner.value
          )}
        </span>
        <span className="text-xs text-muted">
          from <SourceLabel source={entry.winner} secret={secret} />
          {overridden && `, overriding ${entry.definitions.length - 1}`}
        </span>
      </div>
      {open && (
        <ol aria-label={`Definitions of ${entry.name}, from the instance down`} className="pb-2 pl-12">
          {entry.definitions.map((definition, index) => {
            const wins = index === entry.definitions.length - 1;
            return (
              <li
                key={`${definition.kind}-${definition.folderId ?? ''}`}
                className={clsx('flex gap-3 py-0.5 text-xs', wins ? 'text-ink' : 'text-muted line-through')}
              >
                <span className="w-40 truncate">{sourceText(definition, secret)}</span>
                {!secret && <span className="font-mono">{definition.value ?? '—'}</span>}
                {wins && <span className="no-underline">wins</span>}
              </li>
            );
          })}
        </ol>
      )}
    </li>
  );
}

export function ConfigurationExplain({
  scope,
  intro,
}: {
  scope: Scope & { kind: 'folder' | 'automation' };
  intro: string;
}) {
  const configuration = useConfiguration(scope);
  const [query, setQuery] = useState('');
  const needle = query.trim().toLowerCase();
  const matches = (entry: ConfigurationEntry) => needle === '' || entry.name.toLowerCase().includes(needle);
  const variables = configuration.data?.variables.filter(matches) ?? [];
  const secrets = configuration.data?.secrets.filter(matches) ?? [];

  return (
    <section aria-labelledby="where-from" className="rounded-panel border border-line bg-surface">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-line px-4 py-2.5">
        <h3 id="where-from" className="font-semibold">
          Where each value comes from
        </h3>
        <label className="flex min-w-48 items-center gap-1.5 rounded-control border border-line-strong px-2">
          <Search aria-hidden="true" size={13} className="text-muted" />
          <span className="sr-only">Find a name</span>
          <input
            type="search"
            value={query}
            placeholder="Find a name"
            onChange={(event) => setQuery(event.target.value)}
            className="h-7 min-w-0 flex-1 bg-transparent text-sm outline-none placeholder:text-muted"
          />
        </label>
      </div>
      <p className="px-4 pt-2 text-xs text-muted">{intro}</p>
      {configuration.isPending && <Loading label="Loading…" />}
      {configuration.isError && <ErrorAlert error={configuration.error} title="This could not be loaded" />}
      {configuration.data && (
        <>
          <h4 className="px-4 pt-3 text-xs font-medium tracking-wide text-muted uppercase">Variables</h4>
          {variables.length === 0 ? (
            <p className="px-4 py-2 text-sm text-muted">None.</p>
          ) : (
            <ul aria-label="Variables and where they come from" className="divide-y divide-line">
              {variables.map((entry) => (
                <Entry key={entry.name} entry={entry} secret={false} />
              ))}
            </ul>
          )}
          <h4 className="px-4 pt-3 text-xs font-medium tracking-wide text-muted uppercase">Secrets</h4>
          {secrets.length === 0 ? (
            <p className="px-4 py-2 pb-3 text-sm text-muted">None.</p>
          ) : (
            <ul aria-label="Secrets and where they come from" className="divide-y divide-line">
              {secrets.map((entry) => (
                <Entry key={entry.name} entry={entry} secret />
              ))}
            </ul>
          )}
        </>
      )}
    </section>
  );
}
