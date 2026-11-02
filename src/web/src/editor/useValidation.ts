import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { validateAutomation } from '../api/automations';

/** How long typing must pause before the file is checked. */
export const checkDelay = 400;

/** `value`, once it has stopped changing for `delay` milliseconds. */
export function useSettled<T>(value: T, delay: number): T {
  const [settled, setSettled] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setSettled(value), delay);
    return () => clearTimeout(timer);
  }, [value, delay]);
  return settled;
}

export type ValidationStatus = 'checking' | 'valid' | 'invalid' | 'failed';

/**
 * The server's verdict on a file while it is being edited: checked once typing pauses, with the
 * request for an older text cancelled when a newer one starts. The last verdict stays visible
 * while the next is on its way.
 */
export function useValidation(yaml: string, folderId: string, automationId: string | null, enabled = true) {
  const settled = useSettled(yaml, checkDelay);
  const query = useQuery({
    queryKey: ['validate', folderId, automationId, settled],
    queryFn: ({ signal }) => validateAutomation({ yaml: settled, folderId, automationId }, signal),
    enabled,
    placeholderData: keepPreviousData,
    staleTime: Infinity,
    retry: false,
  });

  const pending = settled !== yaml || query.isFetching;
  const status: ValidationStatus = pending
    ? 'checking'
    : query.isError
      ? 'failed'
      : query.data?.valid === false
        ? 'invalid'
        : 'valid';
  return { status, result: query.data, error: query.error };
}
