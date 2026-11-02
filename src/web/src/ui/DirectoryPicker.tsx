/**
 * Finds a person or a group by name (from two characters on): a search field with the matches as
 * buttons below it. Arrow down moves from the field into the matches, the arrows move between them,
 * Enter or a click chooses one.
 */
import { User, Users } from 'lucide-react';
import { useId, useRef, useState, type KeyboardEvent } from 'react';
import { useDirectory } from '../api/admin';

export type DirectoryEntry =
  | { kind: 'user'; id: string; name: string; detail: string }
  | { kind: 'group'; id: string; name: string; detail: string };

export function DirectoryPicker({
  label,
  onPick,
  usersOnly = false,
}: {
  label: string;
  onPick: (entry: DirectoryEntry) => void;
  usersOnly?: boolean;
}) {
  const [query, setQuery] = useState('');
  const id = useId();
  const list = useRef<HTMLUListElement>(null);
  const directory = useDirectory(query);
  const entries: DirectoryEntry[] = [
    ...(directory.data?.users ?? []).map((user): DirectoryEntry => ({
      kind: 'user',
      id: user.id,
      name: user.userName,
      detail: user.displayName,
    })),
    ...(usersOnly ? [] : (directory.data?.groups ?? [])).map((group): DirectoryEntry => ({
      kind: 'group',
      id: group.id,
      name: group.name,
      detail: 'Group',
    })),
  ];
  const searching = query.trim().length >= 2;

  const buttons = () => [...(list.current?.querySelectorAll<HTMLButtonElement>('button') ?? [])];
  const move = (event: KeyboardEvent<HTMLElement>) => {
    const all = buttons();
    const at = all.indexOf(document.activeElement as HTMLButtonElement);
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      all[Math.min(at + 1, all.length - 1)]?.focus();
    } else if (event.key === 'ArrowUp' && at !== -1) {
      event.preventDefault();
      if (at === 0) {
        document.getElementById(id)?.focus();
      } else {
        all[at - 1]?.focus();
      }
    }
  };

  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={id} className="text-sm font-medium">
        {label}
      </label>
      <input
        id={id}
        type="search"
        value={query}
        placeholder="Type at least two letters of a name"
        autoComplete="off"
        aria-describedby={`${id}-status`}
        onChange={(event) => setQuery(event.target.value)}
        onKeyDown={move}
        className="h-9 rounded-control border border-line-strong bg-surface px-2.5 text-sm placeholder:text-muted"
      />
      <p id={`${id}-status`} aria-live="polite" className="text-xs text-muted">
        {!searching
          ? 'Names of people and groups; e-mail addresses are not searched.'
          : directory.isFetching && entries.length === 0
            ? 'Searching…'
            : entries.length === 0
              ? 'Nobody by that name.'
              : `${entries.length} found; arrow down to choose.`}
      </p>
      {searching && entries.length > 0 && (
        <ul
          ref={list}
          aria-label={`${label}: matches`}
          className="max-h-56 overflow-y-auto rounded-control border border-line bg-surface py-1"
        >
          {entries.map((entry) => (
            <li key={`${entry.kind}-${entry.id}`}>
              <button
                type="button"
                onKeyDown={move}
                onClick={() => {
                  onPick(entry);
                  setQuery('');
                }}
                className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm hover:bg-sunken focus:bg-accent-soft focus:outline-none"
              >
                {entry.kind === 'user' ? (
                  <User aria-hidden="true" size={14} className="text-muted" />
                ) : (
                  <Users aria-hidden="true" size={14} className="text-muted" />
                )}
                <span className="font-medium">{entry.name}</span>
                <span className="truncate text-xs text-muted">{entry.detail}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
