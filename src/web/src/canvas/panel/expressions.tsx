/**
 * Values that are expressions, made readable: a field whose whole value names a variable or a
 * secret (`${{ secrets.DEPLOY_TOKEN }}`) shows it as a chip, and a menu beside value fields inserts
 * the variables and secrets that reach the automation, and the run's details, without typing the
 * syntax.
 */
import { clsx } from 'clsx';
import { Braces, KeyRound, Search, Variable } from 'lucide-react';
import { useEffect, useId, useRef, useState, type InputHTMLAttributes } from 'react';
import { usePanel } from './panelContext';
import { referenceOf } from './references';

/** What the run itself offers, for the menu's last group. */
const runValues = [
  'trigger.branch',
  'trigger.event',
  'trigger.sha',
  'run.commit_short_sha',
  'run.id',
  'run.automation_name',
];

/**
 * A text field for a value that may be an expression. A reference to a variable or secret shows as
 * a chip until chosen; the button beside it inserts one (unless the panel only shows).
 */
export function ExpressionInput({
  label,
  value,
  onValue,
  input,
}: {
  /** What the value is, for the chip's and the menu's names: "Value of TOKEN". */
  label: string;
  value: string;
  onValue: (value: string) => void;
  /** The field's own attributes (id, class, aria-*, onBlur, …); `value` and `onChange` are set here. */
  input: InputHTMLAttributes<HTMLInputElement>;
}) {
  const { readOnly } = usePanel();
  const [editing, setEditing] = useState(false);
  const field = useRef<HTMLInputElement>(null);
  const named = editing ? null : referenceOf(value);
  const expression = value.includes('${{');

  useEffect(() => {
    if (editing) {
      field.current?.focus();
    }
  }, [editing]);

  return (
    <div className="flex min-w-0 flex-1 items-center gap-1">
      {named ? (
        <button
          type="button"
          id={input.id}
          aria-label={`${label}: ${named.kind} ${named.name}. Choose it to edit the expression.`}
          title={value}
          onClick={() => setEditing(true)}
          className={clsx(input.className, 'flex min-w-0 items-center gap-1.5 text-left')}
        >
          {named.kind === 'secret' ? (
            <KeyRound aria-hidden="true" size={12} className="shrink-0 text-warn" />
          ) : (
            <Variable aria-hidden="true" size={12} className="shrink-0 text-accent" />
          )}
          <span className="shrink-0 text-muted">{named.kind}</span>
          <span className="truncate font-mono font-medium">{named.name}</span>
        </button>
      ) : (
        <input
          {...input}
          ref={field}
          value={value}
          readOnly={readOnly}
          title={expression ? value : undefined}
          onChange={(event) => onValue(event.target.value)}
          onBlur={(event) => {
            setEditing(false);
            input.onBlur?.(event);
          }}
          className={clsx(input.className, 'min-w-0', expression && 'font-mono text-xs text-accent')}
        />
      )}
      {!readOnly && (
        <InsertMenu
          label={label}
          onInsert={(inserted) => {
            onValue(value.trim() === '' ? inserted : `${value}${inserted}`);
            setEditing(false);
          }}
        />
      )}
    </div>
  );
}

/** The `{x}` button and its list: variables, secrets and the run's details, with a filter. */
function InsertMenu({ label, onInsert }: { label: string; onInsert: (expression: string) => void }) {
  const { references } = usePanel();
  const [open, setOpen] = useState(false);
  const [filter, setFilter] = useState('');
  const box = useRef<HTMLDivElement>(null);
  const listId = useId();

  useEffect(() => {
    if (!open) {
      return undefined;
    }
    const outside = (event: MouseEvent) => {
      if (box.current && !box.current.contains(event.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener('mousedown', outside);
    return () => document.removeEventListener('mousedown', outside);
  }, [open]);

  const wanted = filter.trim().toLowerCase();
  const groups = [
    { title: 'Variables', prefix: 'vars', names: references.variables },
    { title: 'Secrets', prefix: 'secrets', names: references.secrets },
    { title: 'The run', prefix: '', names: runValues },
  ]
    .map(({ title, prefix, names }) => ({
      title,
      prefix,
      names: names.filter((name) => name.toLowerCase().includes(wanted)),
    }))
    .filter((group) => group.names.length > 0);
  const choose = (expression: string) => {
    onInsert(`\${{ ${expression} }}`);
    setOpen(false);
    setFilter('');
  };

  return (
    <div ref={box} className="relative shrink-0">
      <button
        type="button"
        aria-label={`Insert a variable or secret into ${label}`}
        title="Insert a variable, a secret or a detail of the run"
        aria-expanded={open}
        aria-controls={open ? listId : undefined}
        onClick={() => setOpen(!open)}
        className="rounded-control p-1.5 text-muted hover:bg-sunken hover:text-accent"
      >
        <Braces aria-hidden="true" size={13} />
      </button>
      {open && (
        // Esc closes the menu, not the panel around it.
        // oxlint-disable-next-line jsx-a11y/no-static-element-interactions -- the key belongs to the menu as a whole
        <div
          id={listId}
          onKeyDown={(event) => {
            if (event.key === 'Escape') {
              event.stopPropagation();
              event.preventDefault();
              setOpen(false);
            }
          }}
          className="absolute top-full right-0 z-20 mt-1 flex max-h-72 w-64 flex-col rounded-control border border-line bg-surface shadow-lg"
        >
          <div className="relative border-b border-line p-1.5">
            <Search
              aria-hidden="true"
              size={12}
              className="pointer-events-none absolute top-1/2 left-3.5 -translate-y-1/2 text-muted"
            />
            <input
              // oxlint-disable-next-line jsx-a11y/no-autofocus -- the list was just opened to choose from it
              autoFocus
              type="search"
              aria-label="Filter variables and secrets"
              value={filter}
              onChange={(event) => setFilter(event.target.value)}
              placeholder="Filter"
              className="h-7 w-full rounded-control border border-line bg-canvas pr-2 pl-6 text-xs"
            />
          </div>
          <div className="min-h-0 overflow-y-auto py-1">
            {groups.length === 0 && <p className="px-3 py-2 text-xs text-muted">Nothing matches.</p>}
            {groups.map((group) => (
              <div key={group.title}>
                <p className="px-3 pt-1.5 pb-0.5 text-[11px] font-medium text-muted">{group.title}</p>
                <ul aria-label={group.title}>
                  {group.names.map((name) => {
                    const expression = group.prefix ? `${group.prefix}.${name}` : name;
                    return (
                      <li key={expression}>
                        <button
                          type="button"
                          onClick={() => choose(expression)}
                          className="w-full truncate px-3 py-1 text-left font-mono text-xs hover:bg-accent-soft"
                        >
                          {expression}
                        </button>
                      </li>
                    );
                  })}
                </ul>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
