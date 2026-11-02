/**
 * The coverage of a run: the totals, then each coverage report's packages, and a package's classes
 * with the lines no test reached. The server keeps no source code, so lines are shown by number.
 */
import { clsx } from 'clsx';
import { ArrowDownUp, ChevronDown, ChevronRight, Copy } from 'lucide-react';
import { useState } from 'react';
import {
  useCoverageClasses,
  useReport,
  type CoverageTotals,
  type ReportSummary,
  type RunReports,
} from '../../api/reports';
import { ErrorAlert } from '../../ui/Alert';
import { Button } from '../../ui/Button';
import { Loading } from '../../ui/Spinner';
import { coverageText, latestReports, lineCount, parseLines, percent, rangesText } from './reportModel';

/** A bar for a rate, with the number in words beside it for everyone who does not see the bar. */
function Bar({ rate }: { rate: number | string | null | undefined }) {
  const value = rate === null || rate === undefined ? 0 : Number(rate) * 100;
  return (
    <span className="flex items-center gap-2">
      <span aria-hidden="true" className="h-1.5 w-24 overflow-hidden rounded-full bg-sunken">
        <span
          style={{ width: `${value}%` }}
          className={clsx('block h-full', value >= 80 ? 'bg-ok' : value >= 50 ? 'bg-warn' : 'bg-danger')}
        />
      </span>
      <span className="w-12 text-right text-sm tabular-nums">{percent(rate)}</span>
    </span>
  );
}

function Totals({ totals }: { totals: CoverageTotals }) {
  return (
    <dl className="flex flex-wrap gap-x-8 gap-y-2">
      <div>
        <dt className="text-xs text-muted">Lines</dt>
        <dd className="flex items-center gap-2">
          <Bar rate={totals.lineRate} />
          <span className="text-xs text-muted tabular-nums">
            {Number(totals.linesCovered)} of {Number(totals.linesValid)}
          </span>
        </dd>
      </div>
      {Number(totals.branchesValid) > 0 && (
        <div>
          <dt className="text-xs text-muted">Branches</dt>
          <dd className="flex items-center gap-2">
            <Bar rate={totals.branchRate} />
            <span className="text-xs text-muted tabular-nums">
              {Number(totals.branchesCovered)} of {Number(totals.branchesValid)}
            </span>
          </dd>
        </div>
      )}
    </dl>
  );
}

export function CoverageView({ runId, reports }: { runId: string; reports: RunReports }) {
  const { current, earlier } = latestReports(reports.reports.filter((report) => report.kind === 'Coverage'));
  return (
    <div className="mx-auto flex w-full max-w-6xl flex-col gap-4 p-4">
      {reports.coverage && (
        <section aria-label="Coverage of the run" className="rounded-panel border border-line bg-surface px-4 py-3">
          <Totals totals={reports.coverage} />
        </section>
      )}
      {current.map((report) => (
        <CoverageReport key={report.id} runId={runId} report={report} />
      ))}
      {earlier.length > 0 && (
        <p className="text-xs text-muted">
          {earlier.length} coverage reports of earlier attempts are left out, as they do not count.
        </p>
      )}
    </div>
  );
}

type Sort = 'name' | 'coverage';

function CoverageReport({ runId, report }: { runId: string; report: ReportSummary }) {
  const detail = useReport(runId, report.id);
  const [sort, setSort] = useState<Sort>('coverage');
  const [open, setOpen] = useState<number | null>(null);
  const packages = (detail.data?.packages ?? []).toSorted((a, b) =>
    sort === 'name'
      ? a.name.localeCompare(b.name)
      : Number(a.coverage.lineRate ?? 0) - Number(b.coverage.lineRate ?? 0) || a.name.localeCompare(b.name),
  );
  const headingId = `coverage-${report.id}`;

  return (
    <section aria-labelledby={headingId} className="rounded-panel border border-line bg-surface">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1 border-b border-line px-4 py-2.5">
        <h3 id={headingId} className="font-semibold">
          {report.name}
        </h3>
        <span className="text-xs text-muted">
          {report.jobKey} · {report.format}
        </span>
        {report.coverage && <span className="ml-auto text-sm text-muted">{coverageText(report.coverage)}</span>}
      </div>
      {report.error && <p className="px-4 py-2 text-sm text-danger">The file could not be read: {report.error}</p>}
      {report.truncated && (
        <p className="bg-warn-soft px-4 py-2 text-xs text-ink">
          Only part of the classes was kept; the totals count everything.
        </p>
      )}
      {detail.isPending && report.error === null && <Loading label="Loading packages…" />}
      {detail.isError && <ErrorAlert error={detail.error} title="The report could not be loaded" />}
      {packages.length > 0 && (
        <>
          <div className="flex justify-end px-4 pt-2">
            <button
              type="button"
              onClick={() => setSort(sort === 'name' ? 'coverage' : 'name')}
              className="flex items-center gap-1 text-xs text-muted hover:text-ink"
            >
              <ArrowDownUp aria-hidden="true" size={12} />
              {sort === 'name' ? 'Sorted by name' : 'Lowest coverage first'}
            </button>
          </div>
          <ul aria-label={`Packages of ${report.name}`} className="divide-y divide-line">
            {packages.map((pkg) => {
              const index = Number(pkg.index);
              const expanded = open === index;
              return (
                <li key={index}>
                  <button
                    type="button"
                    aria-expanded={expanded}
                    onClick={() => setOpen(expanded ? null : index)}
                    className="flex w-full items-center gap-3 px-4 py-2 text-left hover:bg-sunken"
                  >
                    {expanded ? (
                      <ChevronDown aria-hidden="true" size={14} />
                    ) : (
                      <ChevronRight aria-hidden="true" size={14} />
                    )}
                    <span className="min-w-0 flex-1 truncate font-mono text-sm">{pkg.name}</span>
                    <Bar rate={pkg.coverage.lineRate} />
                  </button>
                  {expanded && <Classes runId={runId} reportId={report.id} pkg={index} />}
                </li>
              );
            })}
          </ul>
        </>
      )}
    </section>
  );
}

function Classes({ runId, reportId, pkg }: { runId: string; reportId: string; pkg: number }) {
  const classes = useCoverageClasses(runId, reportId, pkg);
  const items = classes.data?.pages.flatMap((page) => page.items) ?? [];
  return (
    <div className="border-t border-line bg-canvas px-4 py-2 pl-10">
      {classes.isPending && <Loading label="Loading classes…" />}
      {classes.isError && <ErrorAlert error={classes.error} title="The classes could not be loaded" />}
      {items.length > 0 && (
        <table className="w-full text-sm">
          <thead>
            <tr className="text-left text-xs text-muted">
              <th className="py-1 font-medium">Class</th>
              <th className="py-1 font-medium">Lines</th>
              <th className="py-1 font-medium">Not reached</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {items.map((item) => {
              const ranges = parseLines(item.uncovered);
              return (
                <tr key={Number(item.index)} className="align-top">
                  <td className="py-1.5 pr-3">
                    <span className="block font-mono text-xs">{item.name}</span>
                    <span className="block text-xs text-muted">{item.file}</span>
                  </td>
                  <td className="py-1.5 pr-3">
                    <Bar rate={item.coverage.lineRate} />
                  </td>
                  <td className="py-1.5">
                    {ranges.length === 0 ? (
                      <span className="text-xs text-muted">Every line was reached.</span>
                    ) : (
                      <span className="flex items-start gap-1.5">
                        <span className="font-mono text-xs break-all">
                          {rangesText(ranges)}{' '}
                          <span className="text-muted">
                            ({lineCount(ranges)} {lineCount(ranges) === 1 ? 'line' : 'lines'})
                          </span>
                        </span>
                        <button
                          type="button"
                          aria-label={`Copy the lines of ${item.name} not reached`}
                          title="Copy"
                          onClick={() => void navigator.clipboard?.writeText(item.uncovered)}
                          className="rounded p-0.5 text-muted hover:bg-sunken hover:text-ink"
                        >
                          <Copy aria-hidden="true" size={12} />
                        </button>
                      </span>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
      {classes.hasNextPage && (
        <Button busy={classes.isFetchingNextPage} onClick={() => void classes.fetchNextPage()}>
          Load more classes
        </Button>
      )}
    </div>
  );
}
