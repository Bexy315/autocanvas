/**
 * A job attempt's log: grouped by step, colored as the terminal would, searchable, and followed
 * while it grows. Only the lines in view are drawn (`@tanstack/react-virtual`), so a log of a
 * hundred thousand lines scrolls as smoothly as a short one; search therefore works on the data,
 * not on what is drawn.
 */
import { useVirtualizer } from '@tanstack/react-virtual';
import { clsx } from 'clsx';
import { ArrowDown, ChevronDown, ChevronRight, ChevronUp, Clock, Download, Search, WrapText } from 'lucide-react';
import { useEffect, useMemo, useRef, useState, type CSSProperties, type KeyboardEvent, type ReactNode } from 'react';
import { readSetting, writeSetting } from '../../app/storage';
import type { JobStep } from '../../api/runs';
import { parseAnsi, type AnsiStyle } from './ansi';
import { groupLines, logRows, logText, numberLines, searchLines, type LogRow, type NumberedLine } from './logModel';
import type { AttemptLog } from './useAttemptLog';

export const wrapKey = 'autocanvas.log.wrap';
export const timesKey = 'autocanvas.log.times';

const rowHeight = 20;

/** A request to show a step's lines, from the list of steps; `nonce` repeats the same request. */
export interface StepJump {
  stepIndex: number;
  nonce: number;
}

/**
 * Black and white text on a colored background stay black and white, as in a terminal; on the
 * page's own background they follow the theme (black would vanish on a dark one).
 */
function foreground(style: AnsiStyle): string | undefined {
  if (style.fg === undefined) {
    return undefined;
  }
  const onColor = style.bg !== undefined || style.bgCss !== undefined;
  if (onColor && (style.fg === 0 || style.fg === 8)) {
    return '#000000';
  }
  if (onColor && (style.fg === 7 || style.fg === 15)) {
    return '#ffffff';
  }
  return `var(--log-${style.fg})`;
}

function segmentStyle(style: AnsiStyle): CSSProperties {
  const color = foreground(style);
  return {
    ...(color === undefined ? {} : { color }),
    ...(style.fgCss ? { color: style.fgCss } : {}),
    ...(style.bg === undefined ? {} : { backgroundColor: `var(--log-${style.bg})` }),
    ...(style.bgCss ? { backgroundColor: style.bgCss } : {}),
    ...(style.bold ? { fontWeight: 600 } : {}),
    ...(style.dim ? { opacity: 0.7 } : {}),
    ...(style.italic ? { fontStyle: 'italic' } : {}),
    ...(style.underline ? { textDecoration: 'underline' } : {}),
  };
}

function LineText({ line }: { line: NumberedLine }) {
  const segments = useMemo(() => parseAnsi(line.text), [line.text]);
  return (
    <>
      {segments.map((segment, index) => (
        // Segments are positions in a line that never changes once written.
        // oxlint-disable-next-line react/no-array-index-key -- see above
        <span key={index} style={segmentStyle(segment.style)}>
          {segment.text}
        </span>
      ))}
    </>
  );
}

const lineTime = (time: string) => new Date(time).toLocaleTimeString(undefined, { hour12: false });

export function LogView({
  log,
  steps,
  label,
  jump,
}: {
  log: AttemptLog;
  steps: readonly JobStep[];
  /** What the log is of, for the region's name and the downloaded file. */
  label: string;
  jump?: StepJump | null;
}) {
  const scroller = useRef<HTMLDivElement>(null);
  const [collapsed, setCollapsed] = useState<ReadonlySet<string>>(new Set());
  const [wrap, setWrap] = useState(() => readSetting(wrapKey) === 'on');
  const [times, setTimes] = useState(() => readSetting(timesKey) === 'on');
  const [query, setQuery] = useState('');
  const [current, setCurrent] = useState(0);
  // Following: the view stays at the end while new lines come, until one scrolls up.
  const [following, setFollowing] = useState(true);
  const [linked, setLinked] = useState<number | null>(() => {
    const match = /^#L(\d+)$/.exec(globalThis.location.hash);
    return match ? Number(match[1]) : null;
  });

  const lines = useMemo(() => numberLines(log.chunks), [log.chunks]);
  const groups = useMemo(() => groupLines(lines, steps), [lines, steps]);
  const rows = useMemo(() => logRows(groups, collapsed), [groups, collapsed]);
  const matches = useMemo(() => searchLines(lines, query), [lines, query]);
  const matched = useMemo(() => new Set(matches), [matches]);
  const focusLine = matches[current] ?? null;

  const virtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => scroller.current,
    estimateSize: () => rowHeight,
    overscan: 30,
    // jsdom measures nothing; in a browser the real size replaces this at once.
    initialRect: { width: 800, height: 480 },
  });

  const rowOfLine = (number: number) => rows.findIndex((row) => row.kind === 'line' && row.line.number === number);

  /** Shows a line, opening its step first if it is folded away. */
  const reveal = (number: number) => {
    const line = lines[number - 1];
    const key = line ? groups.find((group) => group.lines.includes(line))?.key : undefined;
    if (key && collapsed.has(key)) {
      const next = new Set(collapsed);
      next.delete(key);
      setCollapsed(next);
      return;
    }
    const index = rowOfLine(number);
    if (index !== -1) {
      setFollowing(false);
      virtualizer.scrollToIndex(index, { align: 'center' });
    }
  };

  // New lines while following: to the end, by scrolling the element itself once it has grown.
  useEffect(() => {
    const element = scroller.current;
    if (element && following && rows.length > 0 && focusLine === null && linked === null) {
      element.scrollTop = element.scrollHeight;
    }
  }, [rows.length, following, focusLine, linked]);

  // A line linked as #L12, once it has arrived.
  useEffect(() => {
    if (linked !== null && linked <= lines.length) {
      const index = rows.findIndex((row) => row.kind === 'line' && row.line.number === linked);
      if (index !== -1) {
        virtualizer.scrollToIndex(index, { align: 'center' });
      }
    }
  }, [linked, lines.length, rows, virtualizer]);

  // A step chosen in the list of steps.
  useEffect(() => {
    if (!jump) {
      return;
    }
    const index = rows.findIndex((row) => row.kind === 'group' && row.group.stepIndex === jump.stepIndex);
    if (index !== -1) {
      virtualizer.scrollToIndex(index, { align: 'start' });
    }
    // Only a new request moves the view, not every new line.
    // oxlint-disable-next-line react/exhaustive-deps -- see above
  }, [jump]);

  const step = (delta: number) => {
    if (matches.length === 0) {
      return;
    }
    const next = (current + delta + matches.length) % matches.length;
    setCurrent(next);
    const number = matches[next];
    if (number !== undefined) {
      reveal(number);
    }
  };

  const toggle = (key: string) => {
    const next = new Set(collapsed);
    if (next.has(key)) {
      next.delete(key);
    } else {
      next.add(key);
    }
    setCollapsed(next);
  };

  const download = () => {
    const url = URL.createObjectURL(new Blob([logText(lines)], { type: 'text/plain' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = `${label.replaceAll(/[^\w.-]+/g, '-')}.log`;
    link.click();
    URL.revokeObjectURL(url);
  };

  const searchKeys = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter') {
      event.preventDefault();
      step(event.shiftKey ? -1 : 1);
    } else if (event.key === 'Escape') {
      setQuery('');
    }
  };

  const status =
    log.status === 'loading'
      ? 'Loading the log…'
      : log.status === 'failed'
        ? 'The log could not be read; trying again.'
        : lines.length === 0
          ? log.status === 'live'
            ? 'Waiting for the first lines…'
            : 'Nothing was written.'
          : null;

  return (
    <section aria-label={`Log of ${label}`} className="flex min-h-0 flex-1 flex-col">
      <div className="flex flex-wrap items-center gap-2 border-b border-line bg-surface px-3 py-1.5">
        <label className="flex min-w-40 flex-1 items-center gap-1.5 rounded-control border border-line-strong px-2">
          <Search aria-hidden="true" size={13} className="text-muted" />
          <span className="sr-only">Search the log</span>
          <input
            type="search"
            value={query}
            placeholder="Search the log"
            onChange={(event) => {
              setQuery(event.target.value);
              setCurrent(0);
            }}
            onKeyDown={searchKeys}
            className="h-7 min-w-0 flex-1 bg-transparent text-sm outline-none placeholder:text-muted"
          />
        </label>
        {query.trim() !== '' && (
          <span aria-live="polite" className="text-xs text-muted">
            {matches.length === 0 ? 'No match' : `${current + 1} of ${matches.length}`}
          </span>
        )}
        <ToolButton label="Previous match (Shift+Enter)" onClick={() => step(-1)} disabled={matches.length === 0}>
          <ChevronUp aria-hidden="true" size={14} />
        </ToolButton>
        <ToolButton label="Next match (Enter)" onClick={() => step(1)} disabled={matches.length === 0}>
          <ChevronDown aria-hidden="true" size={14} />
        </ToolButton>
        <ToolButton
          label="Wrap long lines"
          pressed={wrap}
          onClick={() => {
            writeSetting(wrapKey, wrap ? 'off' : 'on');
            setWrap(!wrap);
          }}
        >
          <WrapText aria-hidden="true" size={14} />
        </ToolButton>
        <ToolButton
          label="Show times"
          pressed={times}
          onClick={() => {
            writeSetting(timesKey, times ? 'off' : 'on');
            setTimes(!times);
          }}
        >
          <Clock aria-hidden="true" size={14} />
        </ToolButton>
        <ToolButton label="Download the log" onClick={download} disabled={lines.length === 0}>
          <Download aria-hidden="true" size={14} />
        </ToolButton>
      </div>
      {(log.truncated || log.missing > 0) && (
        <p className="border-b border-line bg-warn-soft px-3 py-1 text-xs text-ink">
          {log.truncated
            ? 'The log grew past its limit; the runner stopped sending it here.'
            : `${log.missing} parts of the log never arrived.`}
        </p>
      )}
      <div className="relative min-h-0 flex-1">
        <div
          ref={scroller}
          role="log"
          aria-label={`Lines of ${label}`}
          // Keyboard users must be able to reach a scrolling region to scroll it.
          // oxlint-disable-next-line jsx-a11y/no-noninteractive-tabindex -- see above
          tabIndex={0}
          onScroll={(event) => {
            const element = event.currentTarget;
            setFollowing(element.scrollHeight - element.scrollTop - element.clientHeight < rowHeight * 2);
          }}
          className="h-full overflow-auto bg-sunken font-mono text-xs leading-5"
        >
          {status && <p className="px-3 py-2 font-sans text-sm text-muted">{status}</p>}
          <div style={{ height: virtualizer.getTotalSize() }} className="relative w-full">
            {virtualizer.getVirtualItems().map((item) => {
              const row = rows[item.index];
              return row ? (
                <div
                  key={item.key}
                  ref={virtualizer.measureElement}
                  data-index={item.index}
                  style={{ transform: `translateY(${item.start}px)` }}
                  className="absolute top-0 left-0 w-full"
                >
                  <Row
                    row={row}
                    wrap={wrap}
                    times={times}
                    matched={row.kind === 'line' && matched.has(row.line.number)}
                    current={row.kind === 'line' && (row.line.number === focusLine || row.line.number === linked)}
                    onToggle={toggle}
                    onLink={(number) => {
                      globalThis.history.replaceState(null, '', `#L${number}`);
                      setLinked(number);
                    }}
                  />
                </div>
              ) : null;
            })}
          </div>
        </div>
        {!following && lines.length > 0 && log.status === 'live' && (
          <button
            type="button"
            onClick={() => {
              setFollowing(true);
              setLinked(null);
            }}
            className="absolute right-4 bottom-3 flex items-center gap-1 rounded-full bg-accent px-3 py-1 font-sans text-xs font-medium text-accent-ink shadow-md hover:bg-accent-hover"
          >
            <ArrowDown aria-hidden="true" size={12} />
            Jump to the end
          </button>
        )}
      </div>
    </section>
  );
}

function Row({
  row,
  wrap,
  times,
  matched,
  current,
  onToggle,
  onLink,
}: {
  row: LogRow;
  wrap: boolean;
  times: boolean;
  matched: boolean;
  current: boolean;
  onToggle: (key: string) => void;
  onLink: (number: number) => void;
}) {
  if (row.kind === 'group') {
    const { group, collapsed } = row;
    return (
      <button
        type="button"
        aria-expanded={!collapsed}
        onClick={() => onToggle(group.key)}
        className="flex w-full items-center gap-1.5 border-y border-line bg-surface px-2 text-left font-sans text-xs font-semibold hover:bg-canvas"
      >
        {collapsed ? <ChevronRight aria-hidden="true" size={12} /> : <ChevronDown aria-hidden="true" size={12} />}
        <span className="truncate">{group.title}</span>
        <span className="font-normal text-muted">
          {group.lines.length === 1 ? '1 line' : `${group.lines.length} lines`}
        </span>
      </button>
    );
  }
  const { line } = row;
  return (
    <div
      className={clsx(
        'flex gap-3 px-2',
        current ? 'bg-accent-soft' : matched ? 'bg-warn-soft' : 'hover:bg-surface',
        line.source === 'Stderr' && 'border-l-2 border-danger',
        line.source === 'System' && 'text-muted italic',
      )}
    >
      <button
        type="button"
        aria-label={`Link to line ${line.number}`}
        onClick={() => onLink(line.number)}
        className="w-10 shrink-0 text-right text-muted select-none hover:text-accent"
      >
        {line.number}
      </button>
      {times && <span className="shrink-0 text-muted select-none">{lineTime(line.time)}</span>}
      <span className={clsx('min-w-0 flex-1', wrap ? 'break-all whitespace-pre-wrap' : 'whitespace-pre')}>
        <LineText line={line} />
      </span>
    </div>
  );
}

function ToolButton({
  label,
  onClick,
  disabled = false,
  pressed,
  children,
}: {
  label: string;
  onClick: () => void;
  disabled?: boolean;
  pressed?: boolean;
  children: ReactNode;
}) {
  return (
    <button
      type="button"
      title={label}
      aria-label={label}
      aria-pressed={pressed}
      disabled={disabled}
      onClick={onClick}
      className={clsx(
        'flex h-7 min-w-7 items-center justify-center rounded-control px-1 text-muted hover:bg-sunken hover:text-ink disabled:opacity-40',
        pressed && 'bg-accent-soft text-accent',
      )}
    >
      {children}
    </button>
  );
}
