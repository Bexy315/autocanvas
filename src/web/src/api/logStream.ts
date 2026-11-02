/**
 * Following a job attempt's log live, over Server-Sent Events (`…/log/stream`). This is the one
 * call that does not go through `api`: `EventSource` is not `fetch`, so it gets no middleware. It
 * needs none of it: the stream is a GET on the same origin with the session cookie (the CSRF header
 * is only checked on changes), and a session that ended shows on the next regular call.
 *
 * The server sends the log from `after` on, one `chunk` event per chunk with the sequence as its
 * `id`, and an `end` event once the attempt is done. When the connection drops, the browser
 * reconnects by itself and sends the last `id` as `Last-Event-ID`, so the stream continues where
 * it was; when the browser gives up, `onError` says so and the caller may start again.
 */
import type { LogChunk } from './runs';

export interface LogFollower {
  close: () => void;
}

export interface LogHandlers {
  /** The stream is connected; chunks follow as they are written. */
  onOpen?: () => void;
  onChunk: (chunk: LogChunk) => void;
  onEnd: () => void;
  onError: () => void;
}

export function followLog(runId: string, attemptId: string, after: number | null, handlers: LogHandlers): LogFollower {
  const path = `/api/v1/runs/${encodeURIComponent(runId)}/attempts/${encodeURIComponent(attemptId)}/log/stream`;
  const source = new EventSource(after === null ? path : `${path}?after=${after}`);
  source.addEventListener('open', () => handlers.onOpen?.());
  source.addEventListener('chunk', (event) => {
    handlers.onChunk(JSON.parse((event as MessageEvent<string>).data) as LogChunk);
  });
  source.addEventListener('end', () => {
    source.close();
    handlers.onEnd();
  });
  source.addEventListener('error', () => {
    // While the browser reconnects the state is CONNECTING; CLOSED means it gave up (e.g. a 404).
    if (source.readyState === EventSource.CLOSED) {
      handlers.onError();
    }
  });
  return { close: () => source.close() };
}
