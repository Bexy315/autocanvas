import { useEffect, useState } from 'react';
import { followLog } from '../../api/logStream';
import { readLog, type JobAttempt, type LogChunk } from '../../api/runs';
import { lastSequence, mergeChunks } from './logModel';

export type LogStatus = 'loading' | 'live' | 'done' | 'failed';

export interface AttemptLog {
  chunks: ReadonlyMap<number, LogChunk>;
  status: LogStatus;
  /** The runner stopped sending because the log grew past the limit. */
  truncated: boolean;
  /** Chunks that never arrived. */
  missing: number;
}

/** How long to wait before following again after the browser gave up on the stream. */
export const reconnectMs = 3000;

const finished = new Set(['Succeeded', 'Failed', 'Cancelled']);
const empty: ReadonlyMap<number, LogChunk> = new Map();

/**
 * An attempt's log: read at once when the attempt has finished, followed live while it runs (the
 * stream sends what was written so far first). When the stream fails it is started again from the
 * last chunk known, after a pause.
 */
export function useAttemptLog(runId: string, attempt: Pick<JobAttempt, 'id' | 'status'> | null): AttemptLog {
  const [log, setLog] = useState<AttemptLog>({ chunks: empty, status: 'loading', truncated: false, missing: 0 });
  const attemptId = attempt?.id ?? null;
  const done = attempt ? finished.has(attempt.status) : false;
  const [shown, setShown] = useState(attemptId);
  if (shown !== attemptId) {
    setShown(attemptId);
    setLog({ chunks: empty, status: 'loading', truncated: false, missing: 0 });
  }

  useEffect(() => {
    if (attemptId === null) {
      return undefined;
    }
    let stopped = false;
    if (done) {
      const abort = new AbortController();
      readLog(runId, attemptId, null, abort.signal).then(
        (read) =>
          setLog({
            chunks: mergeChunks(empty, read.chunks),
            status: 'done',
            truncated: read.isTruncated,
            missing: read.missing.length,
          }),
        () => !abort.signal.aborted && setLog((current) => ({ ...current, status: 'failed' })),
      );
      return () => abort.abort();
    }

    let known: ReadonlyMap<number, LogChunk> = empty;
    let follower = { close: () => {} };
    let retry: ReturnType<typeof setTimeout> | undefined;
    const follow = () => {
      follower = followLog(runId, attemptId, lastSequence(known), {
        onOpen: () => setLog((current) => ({ ...current, status: 'live' })),
        onChunk: (chunk) => {
          known = mergeChunks(known, [chunk]);
          setLog((current) => ({ ...current, chunks: known, status: 'live' }));
        },
        onEnd: () => setLog((current) => ({ ...current, status: 'done' })),
        onError: () => {
          setLog((current) => ({ ...current, status: 'failed' }));
          if (!stopped) {
            retry = setTimeout(follow, reconnectMs);
          }
        },
      });
    };
    follow();
    return () => {
      stopped = true;
      clearTimeout(retry);
      follower.close();
    };
  }, [runId, attemptId, done]);

  return log;
}
