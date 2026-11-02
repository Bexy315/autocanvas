/**
 * What starts the automation, from its current version: by hand, webhook (with managing its
 * address), schedules with when they fire next and last, and the automation it follows. Triggers
 * themselves are changed in the file; the webhook's address lives on the server.
 */
import { Calendar, Hand, Link2, Pencil, RefreshCw, Trash2, Webhook, Workflow } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { Link } from 'react-router';
import { useTriggers } from '../../api/automations';
import { useCreateWebhook, useDeleteWebhook, useWebhook, type CreatedWebhook } from '../../api/configuration';
import { WebhookCreatedDialog } from '../../config/WebhookCreatedDialog';
import { ConfirmDialog } from '../../dialogs/ConfirmDialog';
import { hasRole } from '../../lib/roles';
import { ErrorAlert } from '../../ui/Alert';
import { Badge } from '../../ui/Badge';
import { Button } from '../../ui/Button';
import { RelativeTime } from '../../ui/RelativeTime';
import { Loading } from '../../ui/Spinner';
import { useAutomationContext } from './context';

function Row({
  Icon,
  title,
  active,
  children,
}: {
  Icon: typeof Hand;
  title: string;
  active: boolean;
  children?: ReactNode;
}) {
  return (
    <li className="flex gap-3 px-4 py-3">
      <Icon
        aria-hidden="true"
        size={18}
        className={active ? 'mt-0.5 shrink-0 text-accent' : 'mt-0.5 shrink-0 text-muted'}
      />
      <div className="flex min-w-0 flex-1 flex-col gap-1">
        <div className="flex flex-wrap items-center gap-2">
          <span className="font-medium">{title}</span>
          <Badge tone={active ? 'ok' : 'neutral'}>{active ? 'On' : 'Off'}</Badge>
        </div>
        {children}
      </div>
    </li>
  );
}

const outcomes = { Success: 'succeeds', Failure: 'fails', Always: 'ends, whatever happened' } as const;

export function TriggersTab() {
  const { automation } = useAutomationContext();
  const triggers = useTriggers(automation.id);
  const editor = `/automations/${automation.id}/edit`;

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between gap-3">
        <p className="text-sm text-muted">From the current version; triggers are changed in the file.</p>
        {hasRole(automation.myRole, 'Editor') && (
          <Link to={editor} className="flex items-center gap-1 text-sm text-accent hover:underline">
            <Pencil aria-hidden="true" size={14} />
            Change in the editor
          </Link>
        )}
      </div>
      {triggers.isPending && <Loading label="Loading triggers…" />}
      {triggers.isError && <ErrorAlert error={triggers.error} title="The triggers could not be loaded" />}
      {triggers.data && (
        <ul aria-label="Triggers" className="divide-y divide-line rounded-panel border border-line bg-surface">
          <Row Icon={Hand} title="By hand" active={triggers.data.manual}>
            <p className="text-sm text-muted">
              {triggers.data.manual ? (
                <>
                  People with the Operator role start it on the{' '}
                  <Link to={`/automations/${automation.id}/runs`} className="text-accent hover:underline">
                    Runs
                  </Link>{' '}
                  tab or through the API.
                </>
              ) : (
                <>
                  Add <code className="font-mono">manual: true</code> to the triggers to allow it.
                </>
              )}
            </p>
          </Row>
          <Row Icon={Webhook} title="Webhook" active={triggers.data.webhook}>
            <WebhookSection
              automationId={automation.id}
              enabled={triggers.data.webhook}
              canManage={hasRole(automation.myRole, 'Operator')}
            />
          </Row>
          <Row Icon={Calendar} title="On a schedule" active={triggers.data.schedules.length > 0}>
            {triggers.data.schedules.length === 0 ? (
              <p className="text-sm text-muted">
                No schedule. Add <code className="font-mono">schedule:</code> with a cron expression or an interval.
              </p>
            ) : (
              <ul aria-label="Schedules" className="flex flex-col gap-1.5">
                {triggers.data.schedules.map((schedule) => (
                  <li
                    key={Number(schedule.position)}
                    className="flex flex-wrap items-baseline gap-x-4 gap-y-0.5 text-sm"
                  >
                    <span className="font-mono">
                      {schedule.schedule}
                      {schedule.timeZone && <span className="text-muted"> ({schedule.timeZone})</span>}
                    </span>
                    <span className="text-muted">
                      next: {schedule.nextAt ? <RelativeTime value={schedule.nextAt} /> : 'never again'}
                    </span>
                    <span className="text-muted">
                      last:{' '}
                      {schedule.lastAt ? (
                        <Link to={`/automations/${automation.id}/runs`} className="hover:underline">
                          <RelativeTime value={schedule.lastAt} />
                        </Link>
                      ) : (
                        'not yet'
                      )}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </Row>
          <Row Icon={Workflow} title="After another automation" active={triggers.data.follows !== null}>
            {triggers.data.follows ? (
              <p className="text-sm">
                When{' '}
                <Link
                  to={`/folders/${triggers.data.follows.folderId}`}
                  className="font-medium text-accent hover:underline"
                >
                  {triggers.data.follows.automation ?? 'an automation that does not exist (yet)'}
                </Link>{' '}
                {outcomes[triggers.data.follows.on]}.
              </p>
            ) : (
              <p className="text-sm text-muted">
                Add <code className="font-mono">automation_completed:</code> to run after another automation.
              </p>
            )}
          </Row>
        </ul>
      )}
    </div>
  );
}

function WebhookSection({
  automationId,
  enabled,
  canManage,
}: {
  automationId: string;
  enabled: boolean;
  canManage: boolean;
}) {
  const webhook = useWebhook(automationId);
  const create = useCreateWebhook(automationId);
  const remove = useDeleteWebhook(automationId);
  const [signed, setSigned] = useState(true);
  const [confirming, setConfirming] = useState<'rotate' | 'delete' | null>(null);
  const [created, setCreated] = useState<CreatedWebhook | null>(null);
  const address = webhook.data?.hint ? webhook.data : null;

  const make = (requireSignature: boolean) =>
    create.mutate(requireSignature, {
      onSuccess: (result) => {
        setConfirming(null);
        setCreated(result);
      },
    });

  return (
    <div className="flex flex-col gap-2 text-sm">
      {!enabled && (
        <p className="text-muted">
          Calls start nothing until the file has <code className="font-mono">webhook: true</code> under triggers.
        </p>
      )}
      {webhook.isPending && <Loading label="Loading the webhook…" />}
      {webhook.isError && <ErrorAlert error={webhook.error} title="The webhook could not be loaded" />}
      {create.isError && <ErrorAlert error={create.error} title="The address could not be created" />}
      {webhook.data &&
        (address ? (
          <div className="flex flex-wrap items-center gap-x-4 gap-y-1">
            <span className="flex items-center gap-1.5">
              <Link2 aria-hidden="true" size={14} className="text-muted" />
              <code className="font-mono">/api/v1/hooks/{address.hint}</code>
            </span>
            <Badge tone={address.requiresSignature ? 'accent' : 'warn'}>
              {address.requiresSignature ? 'Signed calls only' : 'Unsigned calls'}
            </Badge>
            {address.createdAt && (
              <span className="text-xs text-muted">
                created <RelativeTime value={address.createdAt} />
                {address.createdByName && ` by ${address.createdByName}`}
              </span>
            )}
            {canManage && (
              <span className="ml-auto flex gap-2">
                <Button onClick={() => setConfirming('rotate')}>
                  <RefreshCw aria-hidden="true" size={14} />
                  Rotate
                </Button>
                <Button variant="danger" onClick={() => setConfirming('delete')}>
                  <Trash2 aria-hidden="true" size={14} />
                  Delete
                </Button>
              </span>
            )}
          </div>
        ) : canManage ? (
          <div className="flex flex-wrap items-center gap-3">
            <label className="flex items-center gap-2">
              <input
                type="checkbox"
                checked={signed}
                onChange={(event) => setSigned(event.target.checked)}
                className="size-4 accent-(--color-accent)"
              />
              Require a signature
            </label>
            <Button variant="primary" busy={create.isPending} onClick={() => make(signed)}>
              Create address
            </Button>
          </div>
        ) : (
          <p className="text-muted">No address yet; someone with the Operator role creates one.</p>
        ))}
      <ConfirmDialog
        title="Replace the webhook address?"
        open={confirming === 'rotate'}
        onOpenChange={(open) => !open && setConfirming(null)}
        confirmLabel="Create a new address"
        busy={create.isPending}
        error={create.error}
        onConfirm={() => make(address?.requiresSignature ?? signed)}
      >
        The current address stops working at once; callers need the new one
        {address?.requiresSignature ? ' and its secret' : ''}.
      </ConfirmDialog>
      <ConfirmDialog
        title="Delete the webhook address?"
        open={confirming === 'delete'}
        onOpenChange={(open) => !open && setConfirming(null)}
        confirmLabel="Delete address"
        busy={remove.isPending}
        error={remove.error}
        onConfirm={() => remove.mutate(undefined, { onSuccess: () => setConfirming(null) })}
      >
        Calls of it fail from then on.
      </ConfirmDialog>
      {created && <WebhookCreatedDialog created={created} onClose={() => setCreated(null)} />}
    </div>
  );
}
