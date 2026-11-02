import { expect, test, type Page } from '@playwright/test';
import { signIn, snapshot } from './helpers';

const file = `version: 1
name: Shop
triggers:
  manual: true
jobs:
  build:
    runs_on: linux
    steps:
      - run: dotnet test
    reports:
      - {format: trx, path: "**/*.trx", name: unit tests}
      - {format: cobertura, path: "**/coverage.xml", name: coverage}
`;

const csrf = { 'X-AutoCanvas-Request': '1' };
const attemptId = '01a19d00-0000-7000-8000-0000000000a1';
const testsId = '01a19d00-0000-7000-8000-0000000000b1';
const coverageId = '01a19d00-0000-7000-8000-0000000000c1';

function summary(id: string, kind: 'Tests' | 'Coverage') {
  return {
    id,
    jobKey: 'build',
    attemptId,
    name: kind === 'Tests' ? 'unit tests' : 'coverage',
    format: kind === 'Tests' ? 'trx' : 'cobertura',
    kind,
    path: kind === 'Tests' ? 'TestResults/unit.trx' : 'coverage.xml',
    size: 20_480,
    receivedAt: '2026-11-02T10:01:00Z',
    tests: kind === 'Tests' ? { total: 412, passed: 405, failed: 3, skipped: 4, durationMs: 18_400 } : null,
    coverage:
      kind === 'Coverage'
        ? {
            linesCovered: 1640,
            linesValid: 2000,
            branchesCovered: 410,
            branchesValid: 640,
            lineRate: 0.82,
            branchRate: 0.64,
          }
        : null,
    error: null,
    warnings: [],
    truncated: false,
  };
}

const failure = (index: number, suite: string, name: string, message: string) => ({
  index,
  group: 'Shop.Tests',
  suite,
  name,
  fullName: `${suite}.${name}`,
  outcome: 'Failed',
  durationMs: 12 + index,
  message,
  stackTrace: `   at ${suite}.${name}() in /src/Shop.Tests/${suite.split('.').at(-1)}Tests.cs:line 42\n   at System.RuntimeMethodHandle.InvokeMethod()`,
  output: index === 0 ? 'Applying discount SUMMER10 to cart 7' : null,
});

const failures = [
  failure(0, 'Shop.Tests.Cart', 'applies_the_discount', 'Assert.Equal() Failure\nExpected: 90\nActual:   100'),
  failure(1, 'Shop.Tests.Checkout', 'rounds_totals_to_cents', 'Expected 10.01, but found 10.009999.'),
  failure(2, 'Shop.Tests.Payments', 'retries_a_declined_card', 'Timed out after 5000 ms.'),
];

const history = {
  fullName: 'Shop.Tests.Cart.applies_the_discount',
  passed: 14,
  failed: 5,
  skipped: 1,
  entries: Array.from({ length: 20 }, (_, index) => {
    const outcome = index === 7 ? 'Skipped' : [3, 9, 10, 15, 19].includes(index) ? 'Failed' : 'Passed';
    return {
      runId: `01a19d00-0000-7000-8000-${String(index).padStart(12, '0')}`,
      runNumber: index + 1,
      jobKey: 'build',
      attemptId,
      attempt: 1,
      reportId: testsId,
      outcome,
      durationMs: 10 + ((index * 7) % 15),
      message: outcome === 'Failed' ? 'Assert.Equal() Failure' : null,
      reportedAt: new Date(Date.UTC(2026, 9, 14 + index)).toISOString(),
    };
  }),
};

/** Serves the run's reports: no runner takes part in these tests, so none were uploaded. */
async function serveReports(page: Page, runId: string, automationId: string) {
  await page.route(`**/api/v1/runs/${runId}/reports`, (route) =>
    route.fulfill({
      json: {
        tests: { total: 412, passed: 405, failed: 3, skipped: 4, durationMs: 18_400 },
        coverage: summary(coverageId, 'Coverage').coverage,
        reports: [summary(testsId, 'Tests'), summary(coverageId, 'Coverage')],
      },
    }),
  );
  await page.route(`**/api/v1/runs/${runId}/reports/${testsId}/tests**`, (route) =>
    route.fulfill({ json: { items: failures, next: null } }),
  );
  await page.route(`**/api/v1/runs/${runId}/reports/${coverageId}`, (route) =>
    route.fulfill({
      json: {
        report: summary(coverageId, 'Coverage'),
        packages: [
          {
            index: 0,
            name: 'Shop.Cart',
            coverage: {
              linesCovered: 380,
              linesValid: 400,
              branchesCovered: 90,
              branchesValid: 100,
              lineRate: 0.95,
              branchRate: 0.9,
            },
          },
          {
            index: 1,
            name: 'Shop.Checkout',
            coverage: {
              linesCovered: 300,
              linesValid: 500,
              branchesCovered: 80,
              branchesValid: 200,
              lineRate: 0.6,
              branchRate: 0.4,
            },
          },
          {
            index: 2,
            name: 'Shop.Payments',
            coverage: {
              linesCovered: 160,
              linesValid: 400,
              branchesCovered: 40,
              branchesValid: 140,
              lineRate: 0.4,
              branchRate: 0.29,
            },
          },
        ],
      },
    }),
  );
  await page.route(`**/api/v1/runs/${runId}/reports/${coverageId}/coverage/classes**`, (route) =>
    route.fulfill({
      json: {
        items: [
          {
            index: 0,
            package: 2,
            name: 'Shop.Payments.CardGateway',
            file: 'src/Shop/Payments/CardGateway.cs',
            coverage: {
              linesCovered: 40,
              linesValid: 120,
              branchesCovered: 8,
              branchesValid: 40,
              lineRate: 0.33,
              branchRate: 0.2,
            },
            uncovered: '18-24,40,52-80,97',
          },
          {
            index: 1,
            package: 2,
            name: 'Shop.Payments.Refunds',
            file: 'src/Shop/Payments/Refunds.cs',
            coverage: {
              linesCovered: 60,
              linesValid: 60,
              branchesCovered: 10,
              branchesValid: 10,
              lineRate: 1,
              branchRate: 1,
            },
            uncovered: '',
          },
        ],
        next: null,
      },
    }),
  );
  await page.route(`**/api/v1/automations/${automationId}/tests/history**`, (route) =>
    route.fulfill({ json: history }),
  );
}

test("a run's tests and coverage are read, with a test's history", async ({ page }) => {
  await signIn(page);
  const folder = await page.request.post('/api/v1/folders', { data: { name: 'Reports' }, headers: csrf });
  const folderId = ((await folder.json()) as { id: string }).id;
  const created = await page.request.post(`/api/v1/folders/${folderId}/automations`, {
    data: { name: 'shop', yaml: file },
    headers: csrf,
  });
  expect(created.ok()).toBeTruthy();
  const automationId = ((await created.json()) as { id: string }).id;
  const started = await page.request.post(`/api/v1/automations/${automationId}/runs`, { headers: csrf });
  const runId = ((await started.json()) as { id: string }).id;
  await serveReports(page, runId, automationId);

  // The tabs say what the reports found; the failures come first.
  await page.goto(`/runs/${runId}`);
  const views = page.getByRole('navigation', { name: 'Views of the run' });
  await expect(views.getByRole('link', { name: /Tests 3 failed/ })).toBeVisible();
  await expect(views.getByRole('link', { name: /Coverage 82 %/ })).toBeVisible();
  await views.getByRole('link', { name: /Tests/ }).click();
  const tests = page.getByRole('list', { name: 'Tests of unit tests' });
  await expect(tests.getByText('applies_the_discount', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Failed 3' })).toHaveAttribute('aria-pressed', 'true');
  await expect(tests.getByText('Expected: 90')).toBeVisible();

  // A test's history, with the hint that it is flaky.
  await tests.getByRole('button', { name: 'History of Shop.Tests.Cart.applies_the_discount' }).click();
  const panel = page.getByRole('complementary', { name: 'History of Shop.Tests.Cart.applies_the_discount' });
  await expect(panel.getByText('Looks flaky.')).toBeVisible();
  await snapshot(page, 'reports-1-tests');
  await page.keyboard.press('Escape');
  await expect(panel).toHaveCount(0);

  // Coverage by package, and a package's classes with the lines no test reached.
  await views.getByRole('link', { name: /Coverage/ }).click();
  await page.getByRole('button', { name: /Shop\.Payments/ }).click();
  await expect(page.getByText('18–24, 40, 52–80, 97')).toBeVisible();
  await expect(page.getByText('Every line was reached.')).toBeVisible();
  await snapshot(page, 'reports-2-coverage');
});
