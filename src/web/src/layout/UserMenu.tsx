import * as Menu from '@radix-ui/react-dropdown-menu';
import { Check, ChevronDown, LogOut, Monitor, Moon, Sun, UserRound } from 'lucide-react';
import { useNavigate } from 'react-router';
import { useMe, useSignOut } from '../api/session';
import { useTheme, type Scheme } from '../app/themeContext';

const schemes: { value: Scheme; label: string; Icon: typeof Sun }[] = [
  { value: 'system', label: 'System', Icon: Monitor },
  { value: 'light', label: 'Light', Icon: Sun },
  { value: 'dark', label: 'Dark', Icon: Moon },
];

const item =
  'flex h-8 cursor-default select-none items-center gap-2 rounded-control px-2 text-sm outline-none data-[highlighted]:bg-sunken';

/** The signed-in user's menu: account, color scheme, signing out. */
export function UserMenu() {
  const me = useMe();
  const signOut = useSignOut();
  const navigate = useNavigate();
  const { scheme, setScheme } = useTheme();
  const user = me.data?.user;

  if (!user) {
    return null;
  }

  return (
    <Menu.Root>
      <Menu.Trigger
        aria-label={`Account menu for ${user.displayName}`}
        className="flex h-8 items-center gap-2 rounded-control px-2 text-sm hover:bg-sunken"
      >
        <span
          aria-hidden="true"
          className="flex size-6 items-center justify-center rounded-full bg-accent-soft text-xs font-semibold text-accent"
        >
          {user.displayName.slice(0, 1).toUpperCase()}
        </span>
        <span className="hidden max-w-40 truncate sm:inline">{user.displayName}</span>
        <ChevronDown aria-hidden="true" size={14} className="text-muted" />
      </Menu.Trigger>
      <Menu.Portal>
        <Menu.Content
          align="end"
          sideOffset={4}
          className="z-50 min-w-52 rounded-panel border border-line bg-surface p-1 text-ink shadow-lg"
        >
          <div className="px-2 py-1.5">
            <p className="font-medium">{user.displayName}</p>
            <p className="text-xs text-muted">{user.userName}</p>
          </div>
          <Menu.Separator className="my-1 h-px bg-line" />
          <Menu.Item className={item} onSelect={() => void navigate('/account')}>
            <UserRound aria-hidden="true" size={16} />
            Account
          </Menu.Item>
          <Menu.Separator className="my-1 h-px bg-line" />
          <Menu.Label className="px-2 py-1 text-xs text-muted">Color scheme</Menu.Label>
          <Menu.RadioGroup value={scheme} onValueChange={(value) => setScheme(value as Scheme)}>
            {schemes.map(({ value, label, Icon }) => (
              <Menu.RadioItem key={value} value={value} className={item}>
                <Icon aria-hidden="true" size={16} />
                <span className="flex-1">{label}</span>
                <Menu.ItemIndicator>
                  <Check aria-hidden="true" size={14} />
                </Menu.ItemIndicator>
              </Menu.RadioItem>
            ))}
          </Menu.RadioGroup>
          <Menu.Separator className="my-1 h-px bg-line" />
          <Menu.Item
            className={item}
            onSelect={() =>
              signOut.mutate(undefined, { onSettled: () => void navigate('/sign-in', { replace: true }) })
            }
          >
            <LogOut aria-hidden="true" size={16} />
            Sign out
          </Menu.Item>
        </Menu.Content>
      </Menu.Portal>
    </Menu.Root>
  );
}
