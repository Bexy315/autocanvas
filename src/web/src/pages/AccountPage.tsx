import { useState } from 'react';
import { useMe } from '../api/session';
import { Alert } from '../ui/Alert';
import { PasswordForm } from './PasswordForm';

function formatDate(value: string | null | undefined): string {
  return value ? new Date(value).toLocaleString() : 'Never';
}

/** The signed-in user's own account. Sessions and API tokens come with S3 step 9. */
export function AccountPage() {
  const me = useMe();
  const [changed, setChanged] = useState(false);
  const user = me.data?.user;
  if (!user) {
    return null;
  }

  const groups = me.data?.groups ?? [];
  const facts: [string, string][] = [
    ['User name', user.userName],
    ['Display name', user.displayName],
    ['Email', user.email ?? 'Not given'],
    ['Role', user.isAdmin ? 'Administrator' : 'User'],
    ['Groups', groups.length > 0 ? groups.map((group) => group.name).join(', ') : 'None'],
    ['Last sign-in', formatDate(user.lastSignInAt)],
  ];

  return (
    <div className="mx-auto flex max-w-3xl flex-col gap-6 p-6">
      <h1 className="text-xl font-semibold">Account</h1>
      <section aria-labelledby="profile" className="rounded-panel border border-line bg-surface">
        <h2 id="profile" className="border-b border-line px-4 py-2.5 font-semibold">
          Profile
        </h2>
        <dl className="grid grid-cols-[10rem_1fr] gap-x-4 gap-y-2 px-4 py-3">
          {facts.map(([term, value]) => (
            <div key={term} className="contents">
              <dt className="text-muted">{term}</dt>
              <dd>{value}</dd>
            </div>
          ))}
        </dl>
      </section>
      <section aria-labelledby="password" className="rounded-panel border border-line bg-surface">
        <h2 id="password" className="border-b border-line px-4 py-2.5 font-semibold">
          Password
        </h2>
        <div className="flex max-w-sm flex-col gap-4 px-4 py-4">
          {changed && (
            <Alert tone="ok" title="Password changed">
              Your other sessions have ended; this one stays signed in.
            </Alert>
          )}
          <PasswordForm submitLabel="Change password" onChanged={() => setChanged(true)} />
        </div>
      </section>
    </div>
  );
}
