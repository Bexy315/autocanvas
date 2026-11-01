import * as Menu from '@radix-ui/react-dropdown-menu';
import { clsx } from 'clsx';
import { Ellipsis, type LucideIcon } from 'lucide-react';

export interface Action {
  label: string;
  Icon: LucideIcon;
  onSelect: () => void;
  /** Destroys something; shown in the danger color and last. */
  danger?: boolean;
}

/** More actions behind one button, for things done less often than the page's main action. */
export function ActionsMenu({ label, actions }: { label: string; actions: readonly Action[] }) {
  if (actions.length === 0) {
    return null;
  }
  return (
    <Menu.Root>
      <Menu.Trigger
        aria-label={label}
        className="inline-flex h-8 items-center justify-center rounded-control border border-line-strong bg-surface px-2 text-ink hover:bg-sunken"
      >
        <Ellipsis aria-hidden="true" size={16} />
      </Menu.Trigger>
      <Menu.Portal>
        <Menu.Content
          align="end"
          sideOffset={4}
          className="z-50 min-w-44 rounded-panel border border-line bg-surface p-1 text-ink shadow-lg"
        >
          {actions.map(({ label: name, Icon, onSelect, danger }) => (
            <Menu.Item
              key={name}
              onSelect={onSelect}
              className={clsx(
                'flex h-8 cursor-default items-center gap-2 rounded-control px-2 text-sm outline-none select-none data-[highlighted]:bg-sunken',
                danger && 'text-danger',
              )}
            >
              <Icon aria-hidden="true" size={16} />
              {name}
            </Menu.Item>
          ))}
        </Menu.Content>
      </Menu.Portal>
    </Menu.Root>
  );
}
