import { act, cleanup, fireEvent, render, renderHook, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { JobAttempt, LogChunk, RunJobDetail, RunSummary, RunView } from '../api/runs';
import { automationView, folderView, page, twoJobs } from '../test/fixtures';
import { fakeServer, info, json, me, problem, requests, type Handler } from '../test/fakeServer';
import { withLayout } from '../test/layout';
import { renderApp } from '../test/renderApp';
import { LogView } from './log/LogView';
import { mergeChunks } from './log/logModel';
import { reconnectMs, useAttemptLog, type AttemptLog } from './log/useAttemptLog';

// jsdom lays nothing out; the log draws only the lines that fit, so it is given a size to draw into.
withLayout();

const team = folderView('team', 'Team');
const yaml = `${twoJobs}  test:\n    needs: [build]\n    steps:\n      - run: make test\n`;

function summary(number: number, state: RunSummary['state'], name = 'build'): RunSummary {
  return {
    id: `r${number}`,
    number,
    automationId: 'a1',
    automationName: name,
    folderId: 'team',
    state,
    trigger: 'Manual',
    triggeredByName: 'ada',
    createdAt: '2026-11-02T10:00:00Z',
    finishedAt: state === 'Running' ? null : '2026-11-02T10:01:30Z',
  };
}

function runView(state: RunView['state'] = 'Running', extra: Partial<RunView> = {}): RunView {
  return {
    ...summary(1, state),
    automationVersion: 2,
    cancelRequested: false,
    cancelledByName: null,
    yaml,
    jobs: [
      {
        key: 'build',
        name: 'build',
        status: state === 'Running' ? 'Running' : 'Failed',
        activatedCase: null,
        reason: null,
        attempts: 2,
        startedAt: '2026-11-02T10:00:00Z',
        finishedAt: state === 'Running' ? null : '2026-11-02T10:01:30Z',
      },
      {
        key: 'test',
        name: 'test',
        status: state === 'Running' ? 'Pending' : 'Skipped',
        activatedCase: null,
        reason: state === 'Running' ? null : 'build failed',
        attempts: 0,
        startedAt: null,
        finishedAt: null,
      },
    ],
    ...extra,
  };
}

function attempt(number: number, status: JobAttempt['status']): JobAttempt {
  return {
    id: `t${number}`,
    number,
    status,
    runnerId: null,
    runnerName: 'runner-1',
    queuedAt: '2026-11-02T10:00:00Z',
    startedAt: '2026-11-02T10:00:00Z',
    finishedAt: '2026-11-02T10:00:40Z',
    error: number === 1 ? 'The runner went away.' : null,
    warnings: [],
    reports: [],
    steps: [
      {
        index: 0,
        stepId: null,
        name: 'Compile',
        status: 'Failed',
        reason: null,
        exitCode: 2,
        outputs: {},
        error: null,
        startedAt: '2026-11-02T10:00:00Z',
        finishedAt: '2026-11-02T10:00:40Z',
      },
    ],
  };
}

const buildJob: RunJobDetail = {
  key: 'build',
  name: 'build',
  status: 'Failed',
  reason: null,
  outputs: { version: '1.2.3', token: '***' },
  startedAt: '2026-11-02T10:00:00Z',
  finishedAt: '2026-11-02T10:01:30Z',
  attempts: [attempt(1, 'Failed'), attempt(2, 'Failed')],
};

const logOf = (attemptId: string, text: string) => ({
  attemptId,
  chunks: [{ sequence: 1, stepIndex: 0, lines: [{ time: '2026-11-02T10:00:00Z', source: 'Stdout', text }] }],
  next: 1,
  missing: [],
  isComplete: true,
  isTruncated: false,
});

function server(extra: Record<string, Handler> = {}) {
  fakeServer({
    'GET /api/v1/info': () => json(info),
    'GET /api/v1/me': () => json(me()),
    'GET /api/v1/folders': () => json([team]),
    'GET /api/v1/folders/team': () => json(team),
    'GET /api/v1/folders/team/children': () => json(page([])),
    'GET /api/v1/automations/a1': () => json(automationView('a1', 'team'), 200, { ETag: '"2"' }),
    'GET /api/v1/automations/a1/triggers': () => json({ manual: true, webhook: false, schedules: [], follows: null }),
    'GET /api/v1/runs/r1': () => json(runView('Failed')),
    'GET /api/v1/runs/r1/jobs/build': () => json(buildJob),
    'GET /api/v1/runs/r1/attempts/t1/log': () => json(logOf('t1', 'first try')),
    'GET /api/v1/runs/r1/attempts/t2/log': () => json(logOf('t2', 'error: it broke')),
    ...extra,
  });
}

describe('the runs of an automation', () => {
  it('lists them and starts one by hand, opening it', async () => {
    server({
      'GET /api/v1/automations/a1/runs': () => json(page([summary(2, 'Running'), summary(1, 'Failed')], 'more')),
      'POST /api/v1/automations/a1/runs': () => json(runView(), 201),
    });
    const { user } = renderApp('/automations/a1/runs');

    const list = await screen.findByRole('list', { name: 'Runs of Build' });
    expect(within(list).getByRole('link', { name: 'run 2, Running' })).toHaveAttribute('href', '/runs/r2');
    expect(within(list).getAllByText('Started by hand by ada')).toHaveLength(2);
    await user.click(screen.getByRole('button', { name: 'Load older runs' }));
    expect(requests.some((request) => request.url.includes('cursor=more'))).toBe(true);

    await user.click(screen.getByRole('button', { name: 'Run' }));
    expect(await screen.findByRole('heading', { name: 'Run #1' })).toBeInTheDocument();
    expect(globalThis.location.pathname).toBe('/runs/r1');
  });

  it('says when it runs one at a time, and when it cannot be started by hand', async () => {
    server({
      'GET /api/v1/automations/a1/runs': () => json(page([])),
      'POST /api/v1/automations/a1/runs': () => problem(409, 'A run is active.'),
    });
    const { user } = renderApp('/automations/a1/runs');
    await screen.findByRole('heading', { name: 'No runs yet' });
    // Run sits in the page's header, also on the other tabs.
    await user.click(screen.getByRole('button', { name: 'Run' }));
    expect(await screen.findByText('It is running already, and it runs one at a time')).toBeInTheDocument();
    cleanup();

    server({
      'GET /api/v1/automations/a1/runs': () => json(page([])),
      'GET /api/v1/automations/a1/triggers': () => json({ manual: false, webhook: true, schedules: [], follows: null }),
    });
    renderApp('/automations/a1/runs');
    expect(await screen.findByText(/not started by hand/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Run' })).not.toBeInTheDocument();
  });
});

describe('the runs page', () => {
  it('lists the runs of every automation, filtered by state', async () => {
    server({
      'GET /api/v1/runs': (request) =>
        json(
          page(
            new URL(request.url).searchParams.get('state') === 'Failed'
              ? [summary(3, 'Failed', 'deploy')]
              : [summary(4, 'Running', 'site'), summary(3, 'Failed', 'deploy')],
          ),
        ),
    });
    const { user } = renderApp('/runs');
    expect(await screen.findByRole('link', { name: 'site run 4, Running' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Failed' }));
    await waitFor(() => expect(screen.queryByRole('link', { name: 'site run 4, Running' })).not.toBeInTheDocument());
    expect(screen.getByRole('link', { name: 'deploy run 3, Failed' })).toBeInTheDocument();
  });

  it('shows the latest runs on the start page', async () => {
    server({ 'GET /api/v1/runs': () => json(page([summary(5, 'Succeeded', 'site')])) });
    renderApp('/');
    const latest = await screen.findByRole('list', { name: 'Latest runs' });
    expect(within(latest).getByRole('link', { name: 'site run 5, Succeeded' })).toBeInTheDocument();
  });
});

describe('a run', () => {
  it('shows its jobs with their status, and the chosen job with attempts, steps and log', async () => {
    server();
    const { user } = renderApp('/runs/r1');
    expect(await screen.findByRole('heading', { name: 'Run #1' })).toBeInTheDocument();
    expect(screen.getByText(/Started by hand by ada/)).toBeInTheDocument();
    const jobs = screen.getByRole('region', { name: 'Jobs of the run' });
    expect(within(jobs).getByText('Failed in 1 min 30 s after 2 attempts')).toBeInTheDocument();
    expect(within(jobs).getByText('Skipped: build failed')).toBeInTheDocument();

    // The failed job is shown first, its last attempt with its log.
    const job = screen.getByRole('region', { name: 'The chosen job' });
    expect(await within(job).findByText('error: it broke')).toBeInTheDocument();
    expect(within(job).getByText('1.2.3')).toBeInTheDocument();
    expect(within(job).getByText('hidden')).toBeInTheDocument();
    expect(within(job).getByText(/Failed \(exit 2\)/)).toBeInTheDocument();

    await user.click(within(job).getByRole('button', { name: 'Attempt 1 · Failed' }));
    expect(await within(job).findByText('first try')).toBeInTheDocument();
    expect(within(job).getByText('The runner went away.')).toBeInTheDocument();
    expect(globalThis.location.search).toBe('?job=build&attempt=1');
  });

  it('is cancelled while it runs, and started again', async () => {
    let state: RunView['state'] = 'Running';
    server({
      'GET /api/v1/runs/r1': () => json(runView(state)),
      'GET /api/v1/runs/r1/jobs/build': () => json({ ...buildJob, status: 'Running', attempts: [] }),
      'POST /api/v1/runs/r1/cancel': () => {
        state = 'Cancelled';
        return json(runView('Cancelled'));
      },
      'POST /api/v1/automations/a1/runs': () => json({ ...runView(), id: 'r2', number: 2 }, 201),
      'GET /api/v1/runs/r2': () => json({ ...runView(), id: 'r2', number: 2 }),
      'GET /api/v1/runs/r2/jobs/build': () => json({ ...buildJob, status: 'Running', attempts: [] }),
    });
    const { user } = renderApp('/runs/r1');
    expect(await screen.findByText('The job has not started yet.')).toBeInTheDocument();

    await user.click(await screen.findByRole('button', { name: 'Cancel' }));
    expect(await screen.findByText('Run 1 cancelled')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Run again' }));
    expect(await screen.findByRole('heading', { name: 'Run #2' })).toBeInTheDocument();
  });

  it('says when it could not be loaded, and lists jobs when its file cannot be drawn', async () => {
    server({
      'GET /api/v1/runs/r1': () => json(runView('Failed', { yaml: 'jobs: [', automationId: null })),
      'GET /api/v1/runs/r9': () => problem(404, 'The run does not exist, or you may not see it.'),
    });
    renderApp('/runs/r1');
    const jobs = await screen.findByRole('region', { name: 'Jobs of the run' });
    expect(within(jobs).getByRole('button', { name: /test/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Run again' })).not.toBeInTheDocument();

    renderApp('/runs/r9');
    expect(await screen.findByText('The run could not be loaded')).toBeInTheDocument();
  });

  it('names the commit of a repository automation and its branch or pull request', async () => {
    const commit = '0123456789abcdef0123456789abcdef01234567';
    server({
      'GET /api/v1/runs/r1': () =>
        json(
          runView('Succeeded', {
            commit,
            ref: 'refs/pull/12/head',
            commitUrl: `https://github.com/acme/shop/commit/${commit}`,
            pullRequest: 12,
            pullRequestUrl: 'https://github.com/acme/shop/pull/12',
          }),
        ),
      'GET /api/v1/runs/r2': () => json(runView('Succeeded', { id: 'r2', commit, ref: 'refs/heads/main' })),
    });
    renderApp('/runs/r1');

    expect(await screen.findByRole('link', { name: '0123456' })).toHaveAttribute(
      'href',
      `https://github.com/acme/shop/commit/${commit}`,
    );
    expect(screen.getByRole('link', { name: 'pull request #12' })).toHaveAttribute(
      'href',
      'https://github.com/acme/shop/pull/12',
    );

    renderApp('/runs/r2');
    expect(await screen.findByText(/on main/)).toBeInTheDocument();
  });
});

/** An EventSource that the test drives, as the browser's would on the server's events. */
class FakeEventSource {
  static readonly CONNECTING = 0;
  static readonly OPEN = 1;
  static readonly CLOSED = 2;
  static last: FakeEventSource | null = null;
  readyState = FakeEventSource.CONNECTING;
  readonly url: string;
  private readonly listeners = new Map<string, ((event: MessageEvent<string>) => void)[]>();

  constructor(url: string) {
    this.url = url;
    FakeEventSource.last = this;
  }

  addEventListener(type: string, listener: (event: MessageEvent<string>) => void) {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }

  emit(type: string, data = '') {
    this.readyState = type === 'error' ? FakeEventSource.CLOSED : FakeEventSource.OPEN;
    for (const listener of this.listeners.get(type) ?? []) {
      listener(new MessageEvent(type, { data }));
    }
  }

  close() {
    this.readyState = FakeEventSource.CLOSED;
  }
}

const chunkJson = (sequence: number, text: string) =>
  JSON.stringify({ sequence, stepIndex: 0, lines: [{ time: '2026-11-02T10:00:00Z', source: 'Stdout', text }] });

describe('following a log live', () => {
  beforeEach(() => {
    vi.stubGlobal('EventSource', FakeEventSource);
  });
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it('takes chunks as they come, once each, until the end, and starts again where it was', () => {
    vi.useFakeTimers();
    const { result } = renderHook(() => useAttemptLog('r1', { id: 't1', status: 'Running' }));
    const first = FakeEventSource.last;
    expect(first?.url).toBe('/api/v1/runs/r1/attempts/t1/log/stream');
    expect(result.current.status).toBe('loading');

    act(() => first?.emit('open'));
    expect(result.current.status).toBe('live');
    act(() => {
      first?.emit('chunk', chunkJson(1, 'one'));
      first?.emit('chunk', chunkJson(1, 'one'));
      first?.emit('chunk', chunkJson(2, 'two'));
    });
    expect(result.current.chunks.size).toBe(2);

    act(() => first?.emit('error'));
    expect(result.current.status).toBe('failed');
    act(() => {
      vi.advanceTimersByTime(reconnectMs);
    });
    const second = FakeEventSource.last;
    expect(second?.url).toBe('/api/v1/runs/r1/attempts/t1/log/stream?after=2');
    act(() => second?.emit('end'));
    expect(result.current.status).toBe('done');
  });
});

const liveLog = (lines: string[], status: AttemptLog['status'] = 'live'): AttemptLog => ({
  chunks: mergeChunks(
    new Map(),
    lines.map((text, index): LogChunk => ({
      sequence: index + 1,
      stepIndex: index < 2 ? 0 : 1,
      lines: [{ time: '2026-11-02T10:00:00Z', source: index === 3 ? 'Stderr' : 'Stdout', text }],
    })),
  ),
  status,
  truncated: false,
  missing: 0,
});

const viewSteps = [
  { index: 0, name: 'Install' },
  { index: 1, name: 'Test' },
] as unknown as JobAttempt['steps'];

describe('the log view', () => {
  it('searches, folds steps away, wraps, shows times and links lines', () => {
    render(
      <LogView
        log={liveLog(['npm ci', 'done', 'jest', 'FAIL checkout', 'PASS cart'])}
        steps={viewSteps}
        label="build"
      />,
    );
    const lines = screen.getByRole('log', { name: 'Lines of build' });
    expect(within(lines).getByText('FAIL checkout')).toBeInTheDocument();

    fireEvent.change(screen.getByRole('searchbox', { name: 'Search the log' }), { target: { value: 'checkout' } });
    expect(screen.getByText('1 of 1')).toBeInTheDocument();
    fireEvent.keyDown(screen.getByRole('searchbox'), { key: 'Enter' });
    fireEvent.click(screen.getByRole('button', { name: 'Previous match (Shift+Enter)' }));
    fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'nothing like it' } });
    expect(screen.getByText('No match')).toBeInTheDocument();
    fireEvent.keyDown(screen.getByRole('searchbox'), { key: 'Escape' });

    fireEvent.click(within(lines).getByRole('button', { name: /^1\. Install/ }));
    expect(within(lines).queryByText('npm ci')).not.toBeInTheDocument();
    expect(within(lines).getByRole('button', { name: /^1\. Install/ })).toHaveAttribute('aria-expanded', 'false');

    fireEvent.click(screen.getByRole('button', { name: 'Wrap long lines' }));
    expect(screen.getByRole('button', { name: 'Wrap long lines' })).toHaveAttribute('aria-pressed', 'true');
    fireEvent.click(screen.getByRole('button', { name: 'Show times' }));
    expect(within(lines).getAllByText(/\d{1,2}:\d{2}:\d{2}/).length).toBeGreaterThan(0);

    fireEvent.click(within(lines).getByRole('button', { name: 'Link to line 4' }));
    expect(globalThis.location.hash).toBe('#L4');
  });

  it('offers the way back to the end after scrolling up, and downloads the log', () => {
    const created = vi.fn<(blob: Blob) => string>(() => 'blob:log');
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: created, revokeObjectURL: vi.fn<() => void>() }));
    render(<LogView log={liveLog(['a', 'b', 'c'])} steps={viewSteps} label="build" />);
    const lines = screen.getByRole('log');
    Object.defineProperties(lines, {
      scrollHeight: { value: 2000, configurable: true },
      clientHeight: { value: 400, configurable: true },
      scrollTop: { value: 0, configurable: true, writable: true },
    });
    fireEvent.scroll(lines);
    fireEvent.click(screen.getByRole('button', { name: 'Jump to the end' }));
    expect(screen.queryByRole('button', { name: 'Jump to the end' })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Download the log' }));
    expect(created).toHaveBeenCalledTimes(1);
    vi.unstubAllGlobals();
  });

  it('says what there is while nothing was written, and what is missing', () => {
    const { rerender } = render(<LogView log={liveLog([], 'loading')} steps={[]} label="build" />);
    expect(screen.getByText('Loading the log…')).toBeInTheDocument();
    rerender(<LogView log={liveLog([], 'live')} steps={[]} label="build" />);
    expect(screen.getByText('Waiting for the first lines…')).toBeInTheDocument();
    rerender(<LogView log={{ ...liveLog([], 'done'), missing: 2 }} steps={[]} label="build" />);
    expect(screen.getByText('Nothing was written.')).toBeInTheDocument();
    expect(screen.getByText('2 parts of the log never arrived.')).toBeInTheDocument();
    rerender(<LogView log={{ ...liveLog([], 'failed'), truncated: true }} steps={[]} label="build" />);
    expect(screen.getByText('The log could not be read; trying again.')).toBeInTheDocument();
    expect(screen.getByText(/grew past its limit/)).toBeInTheDocument();
  });
});
