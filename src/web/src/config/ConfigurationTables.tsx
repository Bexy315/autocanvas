/**
 * The variables and secrets a folder or an automation reads, in one list each: those defined here,
 * which can be changed here, and those that reach it from above, each with a tag saying where it is
 * defined. A definition here that wins over one above says so, and shows every definition when
 * unfolded. Variable values for those who may see them; secret values never.
 */
import { clsx } from 'clsx';
import { ChevronDown, ChevronRight, KeyRound, Pencil, Plus, Trash2, Variable } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { Link } from 'react-router';
import {
  useConfiguration,
  useDeleteSecret,
  useDeleteVariable,
  useSecrets,
  useSetSecret,
  useSetVariable,
  useVariables,
  type ConfigurationEntry,
  type ConfigurationSource,
  type Scope,
} from '../api/configuration';
import { ConfirmDialog } from '../dialogs/ConfirmDialog';
import { ErrorAlert } from '../ui/Alert';
import { Button } from '../ui/Button';
import { RelativeTime } from '../ui/RelativeTime';
import { Loading } from '../ui/Spinner';
import { ValueDialog } from './ValueDialog';

type Kind = 'variable' | 'secret';
type Place = Scope & { kind: 'folder' | 'automation' };

/** One name as this place reads it. */
interface Row {
  name: string;
  /** The value that applies here: null when hidden (secrets always, variables without the role). */
  value: string | null;
  /** Where the value that applies is defined. */
  from: ConfigurationSource | null;
  /** Defined at this place, so it can be changed or deleted here. */
  here: boolean;
  /** Every definition from the instance down; more than one means this one overrides others. */
  definitions: readonly ConfigurationSource[];
  /** For a definition here: when and by whom it was last set. */
  changed?: { at: string; by: string };
}

/** Whether a definition is the one this place makes itself. */
function isHere(source: ConfigurationSource, place: Place): boolean {
  return place.kind === 'folder'
    ? source.kind === 'Folder' && source.folderId === place.id
    : source.kind === 'Automation';
}

/** Where a definition is, in words, for its tag. */
function originText(source: ConfigurationSource, place: Place, kind: Kind): string {
  if (isHere(source, place)) {
    return place.kind === 'automation' && kind === 'variable' ? 'in the file' : 'here';
  }
  return source.kind === 'Instance' ? 'instance' : `folder ${source.folderName ?? ''}`.trim();
}

/** The rows: the effective names from the explanation, and definitions here it does not know yet. */
function rowsOf(
  entries: readonly ConfigurationEntry[] | undefined,
  own: readonly { name: string; value?: string | null; updatedAt: string; updatedByName: string }[],
  place: Place,
): Row[] {
  const rows = new Map<string, Row>();
  for (const entry of entries ?? []) {
    rows.set(entry.name, {
      name: entry.name,
      value: entry.winner.value ?? null,
      from: entry.winner,
      here: isHere(entry.winner, place),
      definitions: entry.definitions,
    });
  }
  for (const item of own) {
    const row = rows.get(item.name);
    const changed = { at: item.updatedAt, by: item.updatedByName };
    rows.set(
      item.name,
      row
        ? { ...row, here: true, changed }
        : { name: item.name, value: item.value ?? null, from: null, here: true, definitions: [], changed },
    );
  }
  return [...rows.values()].toSorted((a, b) => a.name.localeCompare(b.name));
}

/** The variables a folder reads: its own, changeable by editors, and those from above. */
export function FolderVariables({ folderId, canEdit, hint }: { folderId: string; canEdit: boolean; hint: string }) {
  const scope = { kind: 'folder', id: folderId } as const;
  const own = useVariables(scope);
  const explained = useConfiguration(scope);
  const set = useSetVariable(scope);
  const remove = useDeleteVariable(scope);
  return (
    <ConfigurationList
      kind="variable"
      place={scope}
      hint={hint}
      rows={rowsOf(explained.data?.variables, own.data ?? [], scope)}
      loading={own.isPending || explained.isPending}
      error={own.error ?? explained.error}
      edit={canEdit ? { set, remove } : null}
    />
  );
}

/** The secrets a folder or an automation reads: its own, changeable by editors, and those from above. */
export function PlaceSecrets({ place, canEdit, hint }: { place: Place; canEdit: boolean; hint: string }) {
  const own = useSecrets(place);
  const explained = useConfiguration(place);
  const set = useSetSecret(place);
  const remove = useDeleteSecret(place);
  return (
    <ConfigurationList
      kind="secret"
      place={place}
      hint={hint}
      rows={rowsOf(explained.data?.secrets, own.data ?? [], place)}
      loading={own.isPending || explained.isPending}
      error={own.error ?? explained.error}
      edit={canEdit ? { set, remove } : null}
    />
  );
}

/** The variables an automation reads: those in its file, changed in the editor, and those from above. */
export function AutomationVariables({
  automationId,
  hint,
  action,
}: {
  automationId: string;
  hint: string;
  /** Beside the title: where the file's variables are changed. */
  action?: ReactNode;
}) {
  const scope = { kind: 'automation', id: automationId } as const;
  const explained = useConfiguration(scope);
  return (
    <ConfigurationList
      kind="variable"
      place={scope}
      hint={hint}
      rows={rowsOf(explained.data?.variables, [], scope)}
      loading={explained.isPending}
      error={explained.error}
      edit={null}
      action={action}
    />
  );
}

interface Edit {
  set: {
    mutate: (value: { name: string; value: string }, options: { onSuccess: () => void }) => void;
    isPending: boolean;
    error: unknown;
    reset: () => void;
  };
  remove: { mutate: (name: string, options: { onSuccess: () => void }) => void; isPending: boolean; error: unknown };
}

type Editing = { mode: 'new' } | { mode: 'change'; row: Row } | { mode: 'override'; row: Row } | null;

function ConfigurationList({
  kind,
  place,
  hint,
  rows,
  loading,
  error,
  edit,
  action,
}: {
  kind: Kind;
  place: Place;
  hint: string;
  rows: Row[];
  loading: boolean;
  error: unknown;
  edit: Edit | null;
  action?: ReactNode;
}) {
  const [editing, setEditing] = useState<Editing>(null);
  const [deleting, setDeleting] = useState<string | null>(null);
  const title = kind === 'secret' ? 'Secrets' : 'Variables';
  const Icon = kind === 'secret' ? KeyRound : Variable;

  return (
    <section aria-label={title} className="rounded-panel border border-line bg-surface">
      <div className="flex items-center justify-between gap-3 border-b border-line px-4 py-2.5">
        <h3 className="flex items-center gap-2 font-semibold">
          <Icon aria-hidden="true" size={16} className="text-muted" />
          {title}
        </h3>
        {action}
        {edit && (
          <Button onClick={() => setEditing({ mode: 'new' })}>
            <Plus aria-hidden="true" size={14} />
            {kind === 'secret' ? 'Add secret' : 'Add variable'}
          </Button>
        )}
      </div>
      <p className="px-4 pt-2 text-xs text-muted">{hint}</p>
      {loading && <Loading label={`Loading ${title.toLowerCase()}…`} />}
      {Boolean(error) && <ErrorAlert error={error} title={`The ${title.toLowerCase()} could not be loaded`} />}
      {!loading && !error && rows.length === 0 && (
        <p className="px-4 py-3 text-sm text-muted">No {title.toLowerCase()} reach this place.</p>
      )}
      {rows.length > 0 && (
        <ul aria-label={title} className="divide-y divide-line">
          {rows.map((row) => (
            <RowItem
              key={row.name}
              row={row}
              kind={kind}
              place={place}
              {...(edit
                ? {
                    onChange: () => setEditing({ mode: 'change', row }),
                    onDelete: () => setDeleting(row.name),
                    onOverride: () => setEditing({ mode: 'override', row }),
                  }
                : {})}
            />
          ))}
        </ul>
      )}
      {edit && editing && (
        <ValueDialog
          open
          onOpenChange={(open) => {
            if (!open) {
              edit.set.reset();
              setEditing(null);
            }
          }}
          kind={kind}
          name={editing.mode === 'new' ? null : editing.row.name}
          initialValue={editing.mode === 'change' && kind === 'variable' ? (editing.row.value ?? '') : ''}
          overrides={
            editing.mode === 'override' && editing.row.from ? originText(editing.row.from, place, kind) : undefined
          }
          busy={edit.set.isPending}
          error={edit.set.error}
          onSave={(name, value) => edit.set.mutate({ name, value }, { onSuccess: () => setEditing(null) })}
        />
      )}
      {edit && (
        <ConfirmDialog
          title={`Delete the ${kind} ${deleting ?? ''}?`}
          open={deleting !== null}
          onOpenChange={(open) => !open && setDeleting(null)}
          confirmLabel={kind === 'secret' ? 'Delete secret' : 'Delete variable'}
          busy={edit.remove.isPending}
          error={edit.remove.error}
          onConfirm={() => deleting && edit.remove.mutate(deleting, { onSuccess: () => setDeleting(null) })}
        >
          {kind === 'secret'
            ? 'Its value is gone for good; runs started from now on no longer see it here.'
            : 'Runs started from now on no longer see it here; a definition further up takes over, if there is one.'}
        </ConfirmDialog>
      )}
    </section>
  );
}

function RowItem({
  row,
  kind,
  place,
  onChange,
  onDelete,
  onOverride,
}: {
  row: Row;
  kind: Kind;
  place: Place;
  onChange?: () => void;
  onDelete?: () => void;
  onOverride?: () => void;
}) {
  const [open, setOpen] = useState(false);
  const overridden = row.definitions.slice(0, -1);
  return (
    <li>
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1 px-4 py-2">
        <span className="w-44 truncate font-mono text-sm">{row.name}</span>
        <span className="min-w-0 flex-1 truncate font-mono text-sm">
          {kind === 'secret' ? (
            <span aria-label="Value hidden" className="text-muted">
              ••••••••
            </span>
          ) : row.value === null ? (
            <span className="font-sans text-muted">Hidden: values need the Operator role</span>
          ) : (
            row.value
          )}
        </span>
        <Origin source={row.from} place={place} kind={kind} here={row.here} />
        {overridden.length > 0 && (
          <button
            type="button"
            aria-expanded={open}
            onClick={() => setOpen(!open)}
            aria-label={`Every definition of ${row.name}`}
            className="flex items-center gap-0.5 text-xs text-muted hover:text-ink"
          >
            {open ? <ChevronDown aria-hidden="true" size={12} /> : <ChevronRight aria-hidden="true" size={12} />}
            overrides {overridden.map((source) => originText(source, place, kind)).join(', ')}
          </button>
        )}
        {row.changed && (
          <span className="text-xs text-muted">
            <RelativeTime value={row.changed.at} /> by {row.changed.by}
          </span>
        )}
        {row.here && onChange && onDelete ? (
          <span className="flex">
            <IconButton
              label={kind === 'secret' ? `Replace ${row.name}` : `Change ${row.name}`}
              Icon={Pencil}
              onClick={onChange}
            />
            <IconButton label={`Delete ${row.name}`} Icon={Trash2} danger onClick={onDelete} />
          </span>
        ) : (
          !row.here &&
          onOverride && (
            <button
              type="button"
              onClick={onOverride}
              aria-label={`Override ${row.name} here`}
              className="rounded-control px-2 py-1 text-xs text-accent hover:bg-accent-soft"
            >
              Override here
            </button>
          )
        )}
      </div>
      {open && (
        <ol aria-label={`Definitions of ${row.name}, from the instance down`} className="pb-2 pl-8">
          {row.definitions.map((definition, index) => {
            const wins = index === row.definitions.length - 1;
            return (
              <li
                key={`${definition.kind}-${definition.folderId ?? ''}`}
                className={clsx('flex gap-3 py-0.5 text-xs', wins ? 'text-ink' : 'text-muted line-through')}
              >
                <span className="w-40 truncate">{originText(definition, place, kind)}</span>
                {kind === 'variable' && <span className="font-mono">{definition.value ?? '—'}</span>}
                {wins && <span className="no-underline">applies</span>}
              </li>
            );
          })}
        </ol>
      )}
    </li>
  );
}

/** The tag saying where a value is defined; folders link to their configuration. */
function Origin({
  source,
  place,
  kind,
  here,
}: {
  source: ConfigurationSource | null;
  place: Place;
  kind: Kind;
  here: boolean;
}) {
  const text = source
    ? originText(source, place, kind)
    : place.kind === 'automation' && kind === 'variable'
      ? 'in the file'
      : 'here';
  const className = clsx(
    'shrink-0 rounded-full px-2 py-0.5 text-xs',
    here ? 'bg-accent-soft font-medium text-accent' : 'bg-sunken text-muted',
  );
  return source?.kind === 'Folder' && !here && source.folderId ? (
    <Link to={`/folders/${source.folderId}/configuration`} className={clsx(className, 'hover:underline')}>
      {text}
    </Link>
  ) : (
    <span className={className}>{text}</span>
  );
}

function IconButton({
  label,
  onClick,
  danger = false,
  Icon,
}: {
  label: string;
  onClick: () => void;
  danger?: boolean;
  Icon: typeof Pencil;
}) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      onClick={onClick}
      className={
        danger
          ? 'rounded-control p-1.5 text-muted hover:bg-danger-soft hover:text-danger'
          : 'rounded-control p-1.5 text-muted hover:bg-sunken hover:text-ink'
      }
    >
      <Icon aria-hidden="true" size={14} />
    </button>
  );
}
