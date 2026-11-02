import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { automationKeys } from './automations';
import { api, unwrap } from './client';
import { pageSize } from './folders';
import type { components } from './schema';

export type RunSummary = components['schemas']['RunSummary'];
export type RunView = components['schemas']['RunView'];
export type RunState = components['schemas']['RunState'];
export type RunJobView = components['schemas']['RunJobView'];
export type RunJobDetail = components['schemas']['RunJobDetailView'];
export type JobAttempt = components['schemas']['JobAttemptView'];
export type JobStep = components['schemas']['JobStepView'];
export type LogPage = components['schemas']['LogPage'];
export type LogChunk = components['schemas']['LogChunkView'];
export type LogLine = components['schemas']['LogLine'];

/** How often a run that is still going is read again; the run itself has no live stream. */
export const runPollMs = 2000;

export const runKeys = {
  recent: (state: RunState | null) => ['runs', 'recent', state] as const,
  run: (id: string) => ['runs', id] as const,
  job: (id: string, key: string) => ['runs', id, 'jobs', key] as const,
};

/** An automation's runs, newest first, page by page. */
export function useAutomationRuns(automationId: string) {
  return useInfiniteQuery({
    queryKey: [...automationKeys.runs(automationId), 'all'],
    initialPageParam: null as string | null,
    queryFn: ({ pageParam }) =>
      unwrap(
        api.GET('/api/v1/automations/{automationId}/runs', {
          params: { path: { automationId }, query: { limit: pageSize, ...(pageParam ? { cursor: pageParam } : {}) } },
        }),
      ),
    getNextPageParam: (page) => page.next ?? undefined,
    // Runs still going change state; the list follows them while it is shown.
    refetchInterval: (query) =>
      query.state.data?.pages.some((page) => page.items.some((run) => run.state === 'Running')) ? runPollMs : false,
  });
}

/** The newest runs of every automation the user may see, optionally in one state. */
export function useRecentRuns(state: RunState | null, limit = pageSize) {
  return useInfiniteQuery({
    queryKey: [...runKeys.recent(state), limit],
    initialPageParam: null as string | null,
    queryFn: ({ pageParam }) =>
      unwrap(
        api.GET('/api/v1/runs', {
          params: { query: { limit, ...(state ? { state } : {}), ...(pageParam ? { cursor: pageParam } : {}) } },
        }),
      ),
    getNextPageParam: (page) => page.next ?? undefined,
    refetchInterval: (query) =>
      query.state.data?.pages.some((page) => page.items.some((run) => run.state === 'Running')) ? runPollMs : false,
  });
}

/** A run with its jobs, read again every two seconds while it runs. */
export function useRun(id: string) {
  return useQuery({
    queryKey: runKeys.run(id),
    queryFn: () => unwrap(api.GET('/api/v1/runs/{id}', { params: { path: { id } } })),
    refetchInterval: (query) => (query.state.data?.state === 'Running' ? runPollMs : false),
  });
}

const settled = new Set(['Succeeded', 'Failed', 'Cancelled', 'Skipped']);

/** A job of a run with its attempts and steps, read again while it is not done. */
export function useRunJob(id: string, key: string | null) {
  return useQuery({
    queryKey: runKeys.job(id, key ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/runs/{id}/jobs/{key}', { params: { path: { id, key: key ?? '' } } })),
    enabled: key !== null,
    refetchInterval: (query) => (query.state.data && settled.has(query.state.data.status) ? false : runPollMs),
  });
}

/** Starts a run of the automation by hand; the new run comes back. */
export function useStartRun(automationId: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () =>
      unwrap(api.POST('/api/v1/automations/{automationId}/runs', { params: { path: { automationId } } })),
    onSuccess: async (run) => {
      client.setQueryData(runKeys.run(run.id), run);
      await client.invalidateQueries({ queryKey: automationKeys.runs(automationId) });
      await client.invalidateQueries({ queryKey: ['runs', 'recent'] });
    },
  });
}

/** Cancels a run; its jobs stop, finally jobs with `on_cancel` still run. */
export function useCancelRun(id: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () => unwrap(api.POST('/api/v1/runs/{id}/cancel', { params: { path: { id } } })),
    onSuccess: async (run) => {
      client.setQueryData(runKeys.run(id), run);
      await client.invalidateQueries({ queryKey: ['runs', id, 'jobs'] });
    },
  });
}

/** Reads an attempt's log from `after` on, page by page, until it has all there is so far. */
export async function readLog(runId: string, attemptId: string, after: number | null, signal?: AbortSignal) {
  const chunks: LogChunk[] = [];
  let from = after;
  let last: LogPage | null = null;
  for (;;) {
    // Each page needs the one before: where it ends is where the next starts.
    // oxlint-disable-next-line no-await-in-loop -- pages are read one after another by design
    const page: LogPage = await unwrap(
      api.GET('/api/v1/runs/{id}/attempts/{attemptId}/log', {
        params: { path: { id: runId, attemptId }, query: { limit: 1000, ...(from === null ? {} : { after: from }) } },
        ...(signal ? { signal } : {}),
      }),
    );
    chunks.push(...page.chunks);
    last = page;
    // Caught up: the attempt has written nothing more yet, or has finished.
    if (page.chunks.length === 0 || page.isComplete) {
      break;
    }
    from = Number(page.next);
  }
  return { chunks, isComplete: last.isComplete, isTruncated: last.isTruncated, missing: last.missing };
}
