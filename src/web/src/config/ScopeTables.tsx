/**
 * The variables and secrets defined at one place (the instance, a folder, an automation), with
 * adding, changing and deleting for those who may. A secret's value never leaves the server.
 */
import { KeyRound, Pencil, Plus, Trash2, Variable } from 'lucide-react';
import { useState } from 'react';
import {
  useDeleteSecret,
  useDeleteVariable,
  useSecrets,
  useSetSecret,
  useSetVariable,
  useVariables,
  type Scope,
} from '../api/configuration';
import { ConfirmDialog } from '../dialogs/ConfirmDialog';
import { ErrorAlert } from '../ui/Alert';
import { Button } from '../ui/Button';
import { RelativeTime } from '../ui/RelativeTime';
import { Loading } from '../ui/Spinner';
import { ValueDialog } from './ValueDialog';

type Editing = { kind: 'new' } | { kind: 'change'; name: string; value: string } | null;

function Heading({ Icon, title, children }: { Icon: typeof Variable; title: string; children?: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-3 border-b border-line px-4 py-2.5">
      <h3 className="flex items-center gap-2 font-semibold">
        <Icon aria-hidden="true" size={16} className="text-muted" />
        {title}
      </h3>
      {children}
    </div>
  );
}

function Changed({ at, by }: { at: string; by: string }) {
  return (
    <span className="text-xs text-muted">
      <RelativeTime value={at} /> by {by}
    </span>
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

/** The variables of the instance or of a folder. */
export function VariablesTable({
  scope,
  canEdit,
  hint,
}: {
  scope: Scope & { kind: 'instance' | 'folder' };
  canEdit: boolean;
  hint: string;
}) {
  const variables = useVariables(scope);
  const set = useSetVariable(scope);
  const remove = useDeleteVariable(scope);
  const [editing, setEditing] = useState<Editing>(null);
  const [deleting, setDeleting] = useState<string | null>(null);
  const items = variables.data ?? [];

  return (
    <section aria-label="Variables" className="rounded-panel border border-line bg-surface">
      <Heading Icon={Variable} title="Variables">
        {canEdit && (
          <Button onClick={() => setEditing({ kind: 'new' })}>
            <Plus aria-hidden="true" size={14} />
            Add variable
          </Button>
        )}
      </Heading>
      <p className="px-4 pt-2 text-xs text-muted">{hint}</p>
      {variables.isPending && <Loading label="Loading variables…" />}
      {variables.isError && <ErrorAlert error={variables.error} title="The variables could not be loaded" />}
      {variables.data && items.length === 0 && <p className="px-4 py-3 text-sm text-muted">No variables here.</p>}
      {items.length > 0 && (
        <ul className="divide-y divide-line">
          {items.map((variable) => (
            <li key={variable.name} className="flex flex-wrap items-center gap-x-4 gap-y-1 px-4 py-2">
              <span className="w-48 truncate font-mono text-sm">{variable.name}</span>
              <span className="min-w-0 flex-1 truncate font-mono text-sm">
                {variable.value ?? <span className="font-sans text-muted">Hidden: values need the Operator role</span>}
              </span>
              <Changed at={variable.updatedAt} by={variable.updatedByName} />
              {canEdit && (
                <span className="flex">
                  <IconButton
                    label={`Change ${variable.name}`}
                    Icon={Pencil}
                    onClick={() => setEditing({ kind: 'change', name: variable.name, value: variable.value ?? '' })}
                  />
                  <IconButton
                    label={`Delete ${variable.name}`}
                    Icon={Trash2}
                    danger
                    onClick={() => setDeleting(variable.name)}
                  />
                </span>
              )}
            </li>
          ))}
        </ul>
      )}
      {editing && (
        <ValueDialog
          open
          onOpenChange={(open) => {
            if (!open) {
              set.reset();
              setEditing(null);
            }
          }}
          kind="variable"
          name={editing.kind === 'change' ? editing.name : null}
          initialValue={editing.kind === 'change' ? editing.value : ''}
          busy={set.isPending}
          error={set.error}
          onSave={(name, value) => set.mutate({ name, value }, { onSuccess: () => setEditing(null) })}
        />
      )}
      <ConfirmDialog
        title={`Delete the variable ${deleting ?? ''}?`}
        open={deleting !== null}
        onOpenChange={(open) => !open && setDeleting(null)}
        confirmLabel="Delete variable"
        busy={remove.isPending}
        error={remove.error}
        onConfirm={() => deleting && remove.mutate(deleting, { onSuccess: () => setDeleting(null) })}
      >
        Runs started from now on no longer see it here; a definition further up takes over, if there is one.
      </ConfirmDialog>
    </section>
  );
}

/** The secrets of the instance, a folder or an automation: names only. */
export function SecretsTable({ scope, canEdit, hint }: { scope: Scope; canEdit: boolean; hint: string }) {
  const secrets = useSecrets(scope);
  const set = useSetSecret(scope);
  const remove = useDeleteSecret(scope);
  const [editing, setEditing] = useState<Editing>(null);
  const [deleting, setDeleting] = useState<string | null>(null);
  const items = secrets.data ?? [];

  return (
    <section aria-label="Secrets" className="rounded-panel border border-line bg-surface">
      <Heading Icon={KeyRound} title="Secrets">
        {canEdit && (
          <Button onClick={() => setEditing({ kind: 'new' })}>
            <Plus aria-hidden="true" size={14} />
            Add secret
          </Button>
        )}
      </Heading>
      <p className="px-4 pt-2 text-xs text-muted">{hint}</p>
      {secrets.isPending && <Loading label="Loading secrets…" />}
      {secrets.isError && <ErrorAlert error={secrets.error} title="The secrets could not be loaded" />}
      {secrets.data && items.length === 0 && <p className="px-4 py-3 text-sm text-muted">No secrets here.</p>}
      {items.length > 0 && (
        <ul className="divide-y divide-line">
          {items.map((secret) => (
            <li key={secret.name} className="flex flex-wrap items-center gap-x-4 gap-y-1 px-4 py-2">
              <span className="w-48 truncate font-mono text-sm">{secret.name}</span>
              <span aria-label="Value hidden" className="min-w-0 flex-1 font-mono text-sm text-muted">
                ••••••••
              </span>
              <Changed at={secret.updatedAt} by={secret.updatedByName} />
              {canEdit && (
                <span className="flex">
                  <IconButton
                    label={`Replace ${secret.name}`}
                    Icon={Pencil}
                    onClick={() => setEditing({ kind: 'change', name: secret.name, value: '' })}
                  />
                  <IconButton
                    label={`Delete ${secret.name}`}
                    Icon={Trash2}
                    danger
                    onClick={() => setDeleting(secret.name)}
                  />
                </span>
              )}
            </li>
          ))}
        </ul>
      )}
      {editing && (
        <ValueDialog
          open
          onOpenChange={(open) => {
            if (!open) {
              set.reset();
              setEditing(null);
            }
          }}
          kind="secret"
          name={editing.kind === 'change' ? editing.name : null}
          busy={set.isPending}
          error={set.error}
          onSave={(name, value) => set.mutate({ name, value }, { onSuccess: () => setEditing(null) })}
        />
      )}
      <ConfirmDialog
        title={`Delete the secret ${deleting ?? ''}?`}
        open={deleting !== null}
        onOpenChange={(open) => !open && setDeleting(null)}
        confirmLabel="Delete secret"
        busy={remove.isPending}
        error={remove.error}
        onConfirm={() => deleting && remove.mutate(deleting, { onSuccess: () => setDeleting(null) })}
      >
        Its value is gone for good; runs started from now on no longer see it here.
      </ConfirmDialog>
    </section>
  );
}
