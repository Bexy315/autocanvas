import { useNavigate } from 'react-router';
import { useTriggers, type AutomationView } from '../../api/automations';
import { ApiError } from '../../api/problem';
import { useStartRun } from '../../api/runs';
import { hasRole } from '../../lib/roles';

/**
 * Starting an automation by hand: whether this user may (Operator, and the file allows `manual`),
 * starting it and going to the new run, and what to say when it did not start.
 */
export function useStartByHand(automation: AutomationView) {
  const triggers = useTriggers(automation.id);
  const start = useStartRun(automation.id);
  const navigate = useNavigate();
  return {
    canStart: hasRole(automation.myRole, 'Operator') && triggers.data?.manual === true,
    busy: start.isPending,
    error: start.error,
    errorTitle:
      start.error instanceof ApiError && start.error.status === 409
        ? 'It is running already, and it runs one at a time'
        : 'The run did not start',
    start: () => start.mutate(undefined, { onSuccess: (next) => void navigate(`/runs/${next.id}`) }),
  };
}
