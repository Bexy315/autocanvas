import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { api, unwrap } from './client';
import { runPollMs } from './runs';
import type { components } from './schema';

export type RunReports = components['schemas']['RunReportsView'];
export type ReportSummary = components['schemas']['ReportSummaryView'];
export type ReportDetail = components['schemas']['ReportDetailView'];
export type TestCase = components['schemas']['TestCaseView'];
export type TestOutcome = components['schemas']['TestCaseOutcome'];
export type TestTotals = components['schemas']['TestTotalsView'];
export type CoverageTotals = components['schemas']['CoverageTotalsView'];
export type CoveragePackage = components['schemas']['CoveragePackageView'];
export type CoverageClass = components['schemas']['CoverageClassView'];
export type TestHistory = components['schemas']['TestHistoryView'];
export type TestHistoryEntry = components['schemas']['TestHistoryEntry'];

/** How many tests or classes a page brings. */
export const reportPageSize = 100;

export const reportKeys = {
  run: (runId: string) => ['runs', runId, 'reports'] as const,
  report: (runId: string, reportId: string) => ['runs', runId, 'reports', reportId] as const,
  tests: (runId: string, reportId: string, outcome: string | null) =>
    ['runs', runId, 'reports', reportId, 'tests', outcome] as const,
  classes: (runId: string, reportId: string, pkg: number | null) =>
    ['runs', runId, 'reports', reportId, 'classes', pkg] as const,
  history: (automationId: string, name: string) => ['automations', automationId, 'tests', name] as const,
};

/** A run's reports with totals over each job's latest attempt; read again while the run runs. */
export function useRunReports(runId: string, running: boolean) {
  return useQuery({
    queryKey: reportKeys.run(runId),
    queryFn: () => unwrap(api.GET('/api/v1/runs/{id}/reports', { params: { path: { id: runId } } })),
    refetchInterval: running ? runPollMs : false,
  });
}

/** A coverage report with its packages. */
export function useReport(runId: string, reportId: string) {
  return useQuery({
    queryKey: reportKeys.report(runId, reportId),
    queryFn: () =>
      unwrap(api.GET('/api/v1/runs/{id}/reports/{reportId}', { params: { path: { id: runId, reportId } } })),
  });
}

/** The tests of a report, in the order of the file, optionally only those with one outcome. */
export function useReportTests(runId: string, reportId: string, outcome: TestOutcome | null, enabled = true) {
  return useInfiniteQuery({
    queryKey: reportKeys.tests(runId, reportId, outcome),
    initialPageParam: null as string | null,
    enabled,
    queryFn: ({ pageParam }) =>
      unwrap(
        api.GET('/api/v1/runs/{id}/reports/{reportId}/tests', {
          params: {
            path: { id: runId, reportId },
            query: {
              limit: reportPageSize,
              ...(outcome ? { outcome: outcome.toLowerCase() } : {}),
              ...(pageParam ? { cursor: pageParam } : {}),
            },
          },
        }),
      ),
    getNextPageParam: (page) => page.next ?? undefined,
  });
}

/** The classes of a coverage report's package, with the lines no test reached. */
export function useCoverageClasses(runId: string, reportId: string, pkg: number) {
  return useInfiniteQuery({
    queryKey: reportKeys.classes(runId, reportId, pkg),
    initialPageParam: null as string | null,
    queryFn: ({ pageParam }) =>
      unwrap(
        api.GET('/api/v1/runs/{id}/reports/{reportId}/coverage/classes', {
          params: {
            path: { id: runId, reportId },
            query: { package: pkg, limit: reportPageSize, ...(pageParam ? { cursor: pageParam } : {}) },
          },
        }),
      ),
    getNextPageParam: (page) => page.next ?? undefined,
  });
}

/** The newest results of a test in the automation's runs. */
export function useTestHistory(automationId: string, fullName: string) {
  return useQuery({
    queryKey: reportKeys.history(automationId, fullName),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/automations/{automationId}/tests/history', {
          params: { path: { automationId }, query: { name: fullName, limit: 50 } },
        }),
      ),
  });
}
