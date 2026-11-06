import { clsx } from 'clsx';
import { ChevronRight, Folder, GitBranch, Lock, Workflow } from 'lucide-react';
import { useId, useRef, useState, type KeyboardEvent } from 'react';
import { useChildren, useFolderAutomations, useStartFolders } from '../api/folders';
import type { FolderRole } from '../lib/roles';
import { Spinner } from '../ui/Spinner';

/** What the tree needs to know of a folder; start folders and children both have it. */
export interface TreeFolder {
  id: string;
  name: string;
  isRestricted: boolean;
  myRole: FolderRole;
}

export interface FolderTreeProps {
  /** Accessible name of the tree. */
  label: string;
  /** The highlighted folder: the page's folder, or the picked one. */
  selectedId?: string | undefined;
  /** Folders to show expanded from the start, e.g. the path to the current folder. */
  expandedIds?: readonly string[] | undefined;
  /** What choosing a folder does: open it, or pick it in a dialog. */
  onChoose: (folder: TreeFolder) => void;
  /** Whether a folder can be chosen; the others are shown but disabled (e.g. without the Editor role). */
  canChoose?: (folder: TreeFolder) => boolean;
  /**
   * Shows each open folder's automations after its subfolders, and what choosing one does. Without
   * it the tree holds folders only, as where a folder is picked.
   */
  onChooseAutomation?: ((id: string) => void) | undefined;
}

/**
 * The folders the user may see, as a tree whose subfolders load when a folder is expanded. Follows
 * the ARIA tree pattern: one tab stop; arrows move, expand and collapse; Home and End jump; Enter
 * and Space choose.
 */
const noIds: readonly string[] = [];
const anyFolder = () => true;

export function FolderTree({
  label,
  selectedId,
  expandedIds = noIds,
  onChoose,
  canChoose = anyFolder,
  onChooseAutomation,
}: FolderTreeProps) {
  const roots = useStartFolders();
  // What the user opened or closed; other folders are open when they are on the path to the
  // current one, so opening another folder (e.g. by a link on the page) reveals it in the tree.
  const [toggled, setToggled] = useState<ReadonlyMap<string, boolean>>(() => new Map());
  const [focusedId, setFocusedId] = useState<string | null>(null);
  const tree = useRef<HTMLUListElement>(null);

  const isOpen = (id: string) => toggled.get(id) ?? expandedIds.includes(id);
  const toggle = (id: string, open?: boolean) =>
    setToggled((current) => new Map(current).set(id, open ?? !(current.get(id) ?? expandedIds.includes(id))));

  const onKeyDown = (event: KeyboardEvent<HTMLUListElement>) => {
    const items = [...(tree.current?.querySelectorAll<HTMLElement>('[role="treeitem"]') ?? [])];
    const index = items.findIndex((item) => item === document.activeElement);
    const current = items[index];
    if (!current) {
      return;
    }
    const id = current.dataset['id'] ?? '';
    const focus = (item: HTMLElement | undefined) => {
      if (item) {
        event.preventDefault();
        setFocusedId(item.dataset['id'] ?? null);
        item.focus();
      }
    };
    switch (event.key) {
      case 'ArrowDown':
        focus(items[index + 1]);
        break;
      case 'ArrowUp':
        focus(items[index - 1]);
        break;
      case 'Home':
        focus(items[0]);
        break;
      case 'End':
        focus(items.at(-1));
        break;
      case 'ArrowRight':
        event.preventDefault();
        if (current.getAttribute('aria-expanded') === 'false') {
          toggle(id, true);
        } else if (current.getAttribute('aria-expanded') === 'true') {
          focus(items[index + 1]);
        }
        break;
      case 'ArrowLeft':
        event.preventDefault();
        if (current.getAttribute('aria-expanded') === 'true') {
          toggle(id, false);
        } else {
          const parent = current.parentElement?.closest<HTMLElement>('[role="treeitem"]');
          focus(parent ?? undefined);
        }
        break;
      default:
        break;
    }
  };

  if (roots.isPending) {
    return (
      <p className="flex items-center gap-2 px-3 py-2 text-muted">
        <Spinner size={14} /> Loading folders…
      </p>
    );
  }
  if (roots.isError || roots.data.length === 0) {
    return (
      <p className="px-3 py-2 text-xs text-muted">
        {roots.isError ? 'Folders could not be loaded.' : 'No folders yet.'}
      </p>
    );
  }

  // The one tab stop: the focused item, else the selected one, else the first.
  const tabStop =
    focusedId ?? (roots.data.some((folder) => folder.id === selectedId) ? selectedId : null) ?? roots.data[0]?.id;

  return (
    <ul ref={tree} role="tree" aria-label={label} className="flex flex-col py-1" onKeyDown={onKeyDown}>
      {roots.data.map((folder) => (
        <TreeNode
          key={folder.id}
          folder={folder}
          depth={0}
          isOpen={isOpen}
          selectedId={selectedId}
          tabStop={tabStop ?? selectedId}
          onToggle={toggle}
          onChoose={onChoose}
          canChoose={canChoose}
          onChooseAutomation={onChooseAutomation}
          onFocusItem={setFocusedId}
        />
      ))}
    </ul>
  );
}

interface NodeProps {
  folder: TreeFolder;
  depth: number;
  isOpen: (id: string) => boolean;
  selectedId: string | undefined;
  tabStop: string | undefined;
  onToggle: (id: string, open?: boolean) => void;
  onChoose: (folder: TreeFolder) => void;
  canChoose: (folder: TreeFolder) => boolean;
  onChooseAutomation: ((id: string) => void) | undefined;
  onFocusItem: (id: string) => void;
}

function TreeNode({
  folder,
  depth,
  isOpen,
  selectedId,
  tabStop,
  onToggle,
  onChoose,
  canChoose,
  onChooseAutomation,
  onFocusItem,
}: NodeProps) {
  const open = isOpen(folder.id);
  // Named by its own label only, not by the subfolders inside it.
  const nameId = useId();
  const children = useChildren(folder.id, open);
  const subfolders = children.data?.pages.flatMap((page) => page.items) ?? [];
  const withAutomations = onChooseAutomation !== undefined;
  const automationPages = useFolderAutomations(folder.id, open && withAutomations);
  const automations = withAutomations ? (automationPages.data?.pages.flatMap((page) => page.items) ?? []) : [];
  // Before loading, every folder may have children; once loaded, an empty one shows no arrow.
  const leaf =
    open &&
    children.isSuccess &&
    subfolders.length === 0 &&
    (!withAutomations || (automationPages.isSuccess && automations.length === 0));
  const choosable = canChoose(folder);
  const selected = folder.id === selectedId;

  const choose = () => {
    if (choosable) {
      onChoose(folder);
    }
  };

  return (
    <li
      role="treeitem"
      data-id={folder.id}
      aria-labelledby={nameId}
      aria-level={depth + 1}
      aria-selected={selected}
      aria-disabled={choosable ? undefined : true}
      {...(leaf ? {} : { 'aria-expanded': open })}
      tabIndex={tabStop === folder.id ? 0 : -1}
      onFocus={(event) => {
        if (event.target === event.currentTarget) {
          onFocusItem(folder.id);
        }
      }}
      onKeyDown={(event) => {
        if (event.target === event.currentTarget && (event.key === 'Enter' || event.key === ' ')) {
          event.preventDefault();
          choose();
        }
      }}
      className="outline-none [&:focus-visible>div]:outline-2 [&:focus-visible>div]:outline-focus"
    >
      <div
        className={clsx(
          'flex h-7 items-center gap-1 rounded-control pr-2 text-sm',
          selected ? 'bg-accent-soft font-medium text-accent' : 'text-ink hover:bg-sunken',
          !choosable && 'text-muted',
        )}
        style={{ paddingLeft: `${depth * 14 + 4}px` }}
      >
        <button
          type="button"
          tabIndex={-1}
          aria-hidden="true"
          onClick={() => onToggle(folder.id)}
          className={clsx('flex size-5 shrink-0 items-center justify-center rounded text-muted', leaf && 'invisible')}
        >
          {open && children.isFetching && !children.data ? (
            <Spinner size={12} />
          ) : (
            <ChevronRight size={14} className={clsx('transition-transform', open && 'rotate-90')} />
          )}
        </button>
        <button
          type="button"
          tabIndex={-1}
          onClick={choose}
          disabled={!choosable}
          className="flex min-w-0 flex-1 items-center gap-1.5 text-left disabled:cursor-not-allowed"
        >
          <Folder aria-hidden="true" size={14} className="shrink-0" />
          <span id={nameId} className="truncate">
            {folder.name}
          </span>
          {folder.isRestricted && <Lock aria-label="Restricted" size={12} className="shrink-0 text-muted" />}
        </button>
      </div>
      {open && (subfolders.length > 0 || automations.length > 0) && (
        // The ARIA tree pattern wants role="group" on the list of a treeitem's children.
        // oxlint-disable-next-line jsx-a11y/prefer-tag-over-role
        <ul role="group">
          {subfolders.map((child) => (
            <TreeNode
              key={child.id}
              folder={child}
              depth={depth + 1}
              isOpen={isOpen}
              selectedId={selectedId}
              tabStop={tabStop}
              onToggle={onToggle}
              onChoose={onChoose}
              canChoose={canChoose}
              onChooseAutomation={onChooseAutomation}
              onFocusItem={onFocusItem}
            />
          ))}
          {children.hasNextPage && (
            <li role="none" style={{ paddingLeft: `${(depth + 1) * 14 + 28}px` }}>
              <button
                type="button"
                onClick={() => void children.fetchNextPage()}
                className="h-7 text-xs text-accent hover:underline"
              >
                Show more folders
              </button>
            </li>
          )}
          {onChooseAutomation &&
            automations.map((automation) => (
              <AutomationItem
                key={automation.id}
                id={automation.id}
                title={automation.title}
                fromRepository={automation.repository !== null && automation.repository !== undefined}
                depth={depth + 1}
                selected={automation.id === selectedId}
                tabStop={tabStop}
                onChoose={onChooseAutomation}
                onFocusItem={onFocusItem}
              />
            ))}
          {withAutomations && automationPages.hasNextPage && (
            <li role="none" style={{ paddingLeft: `${(depth + 1) * 14 + 28}px` }}>
              <button
                type="button"
                onClick={() => void automationPages.fetchNextPage()}
                className="h-7 text-xs text-accent hover:underline"
              >
                Show more automations
              </button>
            </li>
          )}
        </ul>
      )}
    </li>
  );
}

/** An automation in its folder: a leaf of the tree, with the icon of where it lives. */
function AutomationItem({
  id,
  title,
  fromRepository,
  depth,
  selected,
  tabStop,
  onChoose,
  onFocusItem,
}: {
  id: string;
  title: string;
  fromRepository: boolean;
  depth: number;
  selected: boolean;
  tabStop: string | undefined;
  onChoose: (id: string) => void;
  onFocusItem: (id: string) => void;
}) {
  const nameId = useId();
  const Icon = fromRepository ? GitBranch : Workflow;
  return (
    <li
      role="treeitem"
      data-id={id}
      aria-labelledby={nameId}
      aria-level={depth + 1}
      aria-selected={selected}
      tabIndex={tabStop === id ? 0 : -1}
      onFocus={(event) => {
        if (event.target === event.currentTarget) {
          onFocusItem(id);
        }
      }}
      onKeyDown={(event) => {
        if (event.target === event.currentTarget && (event.key === 'Enter' || event.key === ' ')) {
          event.preventDefault();
          onChoose(id);
        }
      }}
      className="outline-none [&:focus-visible>div]:outline-2 [&:focus-visible>div]:outline-focus"
    >
      <div
        className={clsx(
          'flex h-7 items-center gap-1 rounded-control pr-2 text-sm',
          selected ? 'bg-accent-soft font-medium text-accent' : 'text-ink hover:bg-sunken',
        )}
        // Where a folder's arrow is, an automation has none: its icon lines up with the folders' icons.
        style={{ paddingLeft: `${depth * 14 + 4 + 24}px` }}
      >
        <button
          type="button"
          tabIndex={-1}
          onClick={() => onChoose(id)}
          className="flex min-w-0 flex-1 items-center gap-1.5 text-left"
        >
          <Icon aria-hidden="true" size={14} className={clsx('shrink-0', !selected && 'text-muted')} />
          <span id={nameId} className="truncate">
            {title}
          </span>
        </button>
      </div>
    </li>
  );
}
