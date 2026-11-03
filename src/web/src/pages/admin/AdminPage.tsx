import { Outlet } from 'react-router';
import { useMe } from '../../api/session';
import { SecretsTable, VariablesTable } from '../../config/ScopeTables';
import { usePageInfo } from '../../layout/pageContext';
import { PageHeader } from '../../ui/PageHeader';
import { TabLinks } from '../../ui/Tabs';
import { NotFoundPage } from '../NotFoundPage';

/**
 * Administration of the instance, for admins: variables and secrets, users, groups, runners and
 * the instance's addresses.
 * Others get "no page here"; the server refuses them either way.
 */
export function AdminPage() {
  const me = useMe();
  usePageInfo({ crumbs: [{ label: 'Administration', to: null }] });
  if (me.data && !me.data.user.isAdmin) {
    return <NotFoundPage />;
  }
  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-5 p-6">
      <PageHeader title="Administration">Settings of this AutoCanvas instance, for admins.</PageHeader>
      <TabLinks
        label="Administration"
        tabs={[
          { to: '/admin', label: 'Variables and secrets', end: true },
          { to: '/admin/users', label: 'Users' },
          { to: '/admin/groups', label: 'Groups' },
          { to: '/admin/runners', label: 'Runners' },
          { to: '/admin/instance', label: 'Instance' },
        ]}
      />
      <Outlet />
    </div>
  );
}

/** The instance's variables and secrets, which every automation reads. */
export function AdminVariables() {
  const scope = { kind: 'instance' } as const;
  return (
    <div className="flex flex-col gap-4">
      <VariablesTable
        scope={scope}
        canEdit
        hint="Read as ${{ vars.NAME }} by every automation; folders and automations override them. Everyone signed in can read them."
      />
      <SecretsTable
        scope={scope}
        canEdit
        hint="Read as ${{ secrets.NAME }} by every automation; folders and automations override them."
      />
    </div>
  );
}
