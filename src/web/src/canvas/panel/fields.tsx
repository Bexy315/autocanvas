/**
 * The side panel's fields. Each shows a value read from the file and reports a new one at once; the
 * panel turns that into a change of the file. Labels, hints and the server's problems are wired up
 * for assistive technology, and every field is read-only while the panel is.
 */
import { clsx } from 'clsx';
import { ChevronRight, Plus, X } from 'lucide-react';
import { useId, useState, type ReactNode } from 'react';
import { usePanel } from './panelContext';

/** How a field looks, without its width: fields side by side set their own. */
const controlLook =
  'rounded-control border bg-surface px-2 text-sm text-ink placeholder:text-muted read-only:bg-sunken disabled:opacity-60';
const control = `w-full ${controlLook}`;

interface FieldFrame {
  label: string;
  hint?: string | undefined;
  problems?: readonly string[] | undefined;
}

/** A label above a control, with the hint and the problems below it. */
const noProblems: readonly string[] = [];

function Frame({
  label,
  hint,
  problems = noProblems,
  children,
}: FieldFrame & { children: (props: { id: string; describedBy?: string; invalid: boolean }) => ReactNode }) {
  const id = useId();
  const invalid = problems.length > 0;
  const describedBy = [hint ? `${id}-hint` : null, invalid ? `${id}-problem` : null].filter(Boolean).join(' ');
  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={id} className="text-xs font-medium text-ink">
        {label}
      </label>
      {children({ id, invalid, ...(describedBy ? { describedBy } : {}) })}
      {hint && (
        <p id={`${id}-hint`} className="text-xs text-muted">
          {hint}
        </p>
      )}
      {invalid && (
        <p id={`${id}-problem`} className="text-xs text-danger">
          {problems.join(' ')}
        </p>
      )}
    </div>
  );
}

const border = (invalid: boolean) => (invalid ? 'border-danger' : 'border-line-strong');

export function TextInput({
  value,
  onValue,
  onCommit,
  placeholder,
  mono = false,
  ...frame
}: FieldFrame & {
  value: string;
  onValue: (value: string) => void;
  /** For values written only when done, such as a job's id: on Enter and when the field is left. */
  onCommit?: () => void;
  placeholder?: string;
  mono?: boolean;
}) {
  const { readOnly } = usePanel();
  return (
    <Frame {...frame}>
      {({ id, describedBy, invalid }) => (
        <input
          id={id}
          value={value}
          placeholder={placeholder}
          readOnly={readOnly}
          aria-invalid={invalid || undefined}
          aria-describedby={describedBy}
          onChange={(event) => onValue(event.target.value)}
          onBlur={onCommit}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              onCommit?.();
            }
          }}
          className={clsx(control, 'h-8', border(invalid), mono && 'font-mono text-xs')}
        />
      )}
    </Frame>
  );
}

/** Several lines, such as a script; it grows with its content. */
export function TextArea({
  value,
  onValue,
  placeholder,
  ...frame
}: FieldFrame & { value: string; onValue: (value: string) => void; placeholder?: string }) {
  const { readOnly } = usePanel();
  const rows = Math.min(24, Math.max(3, value.split('\n').length + 1));
  return (
    <Frame {...frame}>
      {({ id, describedBy, invalid }) => (
        <textarea
          id={id}
          value={value}
          rows={rows}
          placeholder={placeholder}
          readOnly={readOnly}
          spellCheck={false}
          aria-invalid={invalid || undefined}
          aria-describedby={describedBy}
          onChange={(event) => onValue(event.target.value)}
          className={clsx(control, 'resize-y py-1.5 font-mono text-xs leading-5', border(invalid))}
        />
      )}
    </Frame>
  );
}

/**
 * A whole number, or an expression in its place: what is not a number is written as text, and the
 * server says whether it may be.
 */
export function NumberInput({
  value,
  onValue,
  placeholder,
  ...frame
}: FieldFrame & {
  value: unknown;
  onValue: (value: number | string | undefined) => void;
  placeholder?: string;
}) {
  return (
    <TextInput
      {...frame}
      value={value === undefined || value === null ? '' : String(value)}
      {...(placeholder === undefined ? {} : { placeholder })}
      onValue={(text) => {
        const trimmed = text.trim();
        onValue(trimmed === '' ? undefined : /^-?\d+$/.test(trimmed) ? Number(trimmed) : text);
      }}
    />
  );
}

export function Toggle({
  label,
  hint,
  checked,
  onChecked,
}: {
  label: string;
  hint?: string;
  checked: boolean;
  onChecked: (checked: boolean) => void;
}) {
  const { readOnly } = usePanel();
  const id = useId();
  return (
    <div className="flex flex-col gap-0.5">
      <label htmlFor={id} className="flex items-center gap-2 text-sm text-ink">
        <input
          id={id}
          type="checkbox"
          checked={checked}
          disabled={readOnly}
          aria-describedby={hint ? `${id}-hint` : undefined}
          onChange={(event) => onChecked(event.target.checked)}
          className="size-4 accent-(--color-accent)"
        />
        {label}
      </label>
      {hint && (
        <p id={`${id}-hint`} className="pl-6 text-xs text-muted">
          {hint}
        </p>
      )}
    </div>
  );
}

export interface Option {
  value: string;
  label: string;
}

/** One of a few values; `empty` names the choice of leaving it unset. */
export function Choice({
  value,
  options,
  onValue,
  empty,
  ...frame
}: FieldFrame & { value: string; options: readonly Option[]; onValue: (value: string) => void; empty?: string }) {
  const { readOnly } = usePanel();
  const known = value === '' || options.some((option) => option.value === value);
  return (
    <Frame {...frame}>
      {({ id, describedBy, invalid }) => (
        <select
          id={id}
          value={value}
          disabled={readOnly}
          aria-invalid={invalid || undefined}
          aria-describedby={describedBy}
          onChange={(event) => onValue(event.target.value)}
          className={clsx(control, 'h-8', border(invalid))}
        >
          {empty !== undefined && <option value="">{empty}</option>}
          {!known && <option value={value}>{value}</option>}
          {options.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      )}
    </Frame>
  );
}

/** A list of short texts, such as runner labels: Enter adds what is typed, the × removes one. */
export function TagList({
  values,
  onValues,
  placeholder,
  ...frame
}: FieldFrame & { values: readonly string[]; onValues: (values: string[]) => void; placeholder?: string }) {
  const { readOnly } = usePanel();
  const [typed, setTyped] = useState('');
  const add = () => {
    const value = typed.trim();
    if (value !== '' && !values.includes(value)) {
      onValues([...values, value]);
    }
    setTyped('');
  };
  return (
    <Frame {...frame}>
      {({ id, describedBy, invalid }) => (
        <div className={clsx('flex flex-wrap items-center gap-1 rounded-control border px-1 py-1', border(invalid))}>
          {values.map((value) => (
            <span key={value} className="flex items-center gap-0.5 rounded bg-sunken px-1.5 py-0.5 font-mono text-xs">
              {value}
              {!readOnly && (
                <button
                  type="button"
                  aria-label={`Remove ${value}`}
                  onClick={() => onValues(values.filter((other) => other !== value))}
                  className="rounded text-muted hover:text-ink"
                >
                  <X aria-hidden="true" size={12} />
                </button>
              )}
            </span>
          ))}
          {!readOnly && (
            <input
              id={id}
              value={typed}
              placeholder={values.length === 0 ? placeholder : 'Add…'}
              aria-invalid={invalid || undefined}
              aria-describedby={describedBy}
              onChange={(event) => setTyped(event.target.value)}
              onBlur={add}
              onKeyDown={(event) => {
                if (event.key === 'Enter' || event.key === ',') {
                  event.preventDefault();
                  add();
                } else if (event.key === 'Backspace' && typed === '' && values.length > 0) {
                  onValues(values.slice(0, -1));
                }
              }}
              className="h-6 min-w-24 flex-1 bg-transparent px-1 text-sm outline-none placeholder:text-muted"
            />
          )}
        </div>
      )}
    </Frame>
  );
}

type Entries = Readonly<Record<string, string | number | boolean>>;

/**
 * Names with values, such as environment variables. Values are written as they are typed; a name is
 * written when the field is left, so a name half typed never overwrites another entry.
 */
export function KeyValueList({
  label,
  entries,
  onEntries,
  hint,
  keyLabel = 'Name',
  valueLabel = 'Value',
}: {
  label: string;
  entries: Entries;
  onEntries: (entries: Record<string, string | number | boolean>) => void;
  hint?: string;
  keyLabel?: string;
  valueLabel?: string;
}) {
  const { readOnly } = usePanel();
  const [adding, setAdding] = useState(false);
  const pairs = Object.entries(entries);
  const rename = (old: string, next: string) => {
    const name = next.trim();
    if (name === old || name === '' || name in entries) {
      return;
    }
    onEntries(Object.fromEntries(pairs.map(([key, value]) => [key === old ? name : key, value])));
  };
  return (
    <fieldset className="flex flex-col gap-1.5">
      <legend className="mb-1 text-xs font-medium text-ink">{label}</legend>
      {hint && <p className="text-xs text-muted">{hint}</p>}
      {pairs.map(([key, value]) => (
        <div key={key} className="flex items-center gap-1">
          <input
            aria-label={`${keyLabel} of ${key}`}
            defaultValue={key}
            readOnly={readOnly}
            onBlur={(event) => rename(key, event.target.value)}
            className={clsx(controlLook, 'h-8 w-2/5 shrink-0 border-line-strong font-mono text-xs')}
          />
          <input
            aria-label={`${valueLabel} of ${key}`}
            value={String(value)}
            readOnly={readOnly}
            onChange={(event) => onEntries({ ...entries, [key]: event.target.value })}
            className={clsx(controlLook, 'h-8 min-w-0 flex-1 border-line-strong font-mono text-xs')}
          />
          {!readOnly && (
            <RemoveButton
              label={`Remove ${key}`}
              onRemove={() => onEntries(Object.fromEntries(pairs.filter(([other]) => other !== key)))}
            />
          )}
        </div>
      ))}
      {!readOnly &&
        (adding ? (
          <input
            // oxlint-disable-next-line jsx-a11y/no-autofocus -- the field appears because "Add" was just chosen
            autoFocus
            aria-label={`New ${keyLabel.toLowerCase()}`}
            placeholder={keyLabel}
            onBlur={(event) => {
              const name = event.target.value.trim();
              if (name !== '' && !(name in entries)) {
                onEntries({ ...entries, [name]: '' });
              }
              setAdding(false);
            }}
            onKeyDown={(event) => {
              if (event.key === 'Enter') {
                event.currentTarget.blur();
              } else if (event.key === 'Escape') {
                // Only this field goes; the panel stays open.
                event.stopPropagation();
                setAdding(false);
              }
            }}
            className={clsx(controlLook, 'h-8 w-2/5 shrink-0 border-line-strong font-mono text-xs')}
          />
        ) : (
          <AddButton label={`Add ${keyLabel.toLowerCase()}`} onAdd={() => setAdding(true)} />
        ))}
    </fieldset>
  );
}

export function AddButton({ label, onAdd }: { label: string; onAdd: () => void }) {
  return (
    <button
      type="button"
      onClick={onAdd}
      className="flex w-fit items-center gap-1 rounded-control px-1.5 py-1 text-xs font-medium text-accent hover:bg-accent-soft"
    >
      <Plus aria-hidden="true" size={12} />
      {label}
    </button>
  );
}

export function RemoveButton({ label, onRemove }: { label: string; onRemove: () => void }) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      onClick={onRemove}
      className="shrink-0 rounded-control p-1.5 text-muted hover:bg-danger-soft hover:text-danger"
    >
      <X aria-hidden="true" size={14} />
    </button>
  );
}

/** A group of fields that folds away; folded, it says what it holds in one line. */
export function Section({
  title,
  summary,
  open: initiallyOpen = false,
  problems = 0,
  children,
}: {
  title: string;
  summary?: string;
  open?: boolean;
  problems?: number;
  children: ReactNode;
}) {
  const [open, setOpen] = useState(initiallyOpen || problems > 0);
  return (
    <section className="border-b border-line">
      <h3>
        <button
          type="button"
          aria-expanded={open}
          onClick={() => setOpen(!open)}
          className="flex w-full items-center gap-1.5 px-4 py-2.5 text-left text-sm font-semibold hover:bg-sunken"
        >
          <ChevronRight aria-hidden="true" size={14} className={clsx('shrink-0', open && 'rotate-90')} />
          <span>{title}</span>
          {problems > 0 && <span className="text-xs font-normal text-danger">({problems})</span>}
          {!open && summary && <span className="ml-auto truncate text-xs font-normal text-muted">{summary}</span>}
        </button>
      </h3>
      {open && <div className="flex flex-col gap-3 px-4 pt-1 pb-4">{children}</div>}
    </section>
  );
}

/** A value the panel cannot show as a form: as it is written, to be changed in the YAML. */
export function YamlOnly({ label, source }: { label: string; source: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs font-medium text-ink">{label}</span>
      <pre className="overflow-x-auto rounded-control bg-sunken px-2 py-1.5 font-mono text-xs">{source}</pre>
      <p className="text-xs text-muted">Edit this in the YAML; it is written in a form the panel does not show.</p>
    </div>
  );
}
