import { useMe } from '../../api/session';
import { SecretsTable, VariablesTable } from '../../config/ScopeTables';
import { usePageInfo } from '../../layout/pageContext';
import { PageHeader } from '../../ui/PageHeader';
import { NotFoundPage } from '../NotFoundPage';

/**
 * Administration: the instance's variables and secrets, which every automation reads. Users,
 * groups and runners join it in S3 step 9. Admins only; the server refuses changes from others.
 */
export function AdminPage() {
  const me = useMe();
  usePageInfo({ crumbs: [{ label: 'Administration', to: null }] });
  if (me.data && !me.data.user.isAdmin) {
    return <NotFoundPage />;
  }
  const scope = { kind: 'instance' } as const;
  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-5 p-6">
      <PageHeader title="Administration">Settings of this AutoCanvas instance, for admins.</PageHeader>
      <h2 className="text-base font-semibold">Variables and secrets</h2>
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
