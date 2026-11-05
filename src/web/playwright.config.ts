import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { defineConfig, devices } from '@playwright/test';

/**
 * The browser tests run against the real server with the built UI in its wwwroot (`npm run build`
 * first): SQLite in a fresh directory, a first admin who must change the password, ports of their
 * own so a server started for development can keep running.
 */
export const adminUserName = 'admin';
export const adminPassword = 'initial password for e2e';
const port = 5090;
const dataDirectory = mkdtempSync(join(tmpdir(), 'autocanvas-e2e-'));

const serverArguments = [
  // In CI on Linux the server also listens on the Docker bridge, where containers (Gitea calling its
  // webhook, e2e/15) reach it; elsewhere Docker Desktop's host.docker.internal reaches 127.0.0.1.
  process.env['CI'] && process.platform === 'linux'
    ? `"--urls=http://127.0.0.1:${port};http://172.17.0.1:${port}"`
    : `--urls=http://127.0.0.1:${port}`,
  '--Runners:Urls=http://127.0.0.1:5091',
  `--Server:DataDirectory=${dataDirectory}`,
  `--Bootstrap:AdminUserName=${adminUserName}`,
  `--Bootstrap:AdminPassword="${adminPassword}"`,
  // The development key from launchSettings.json: 32 zero bytes, for throwaway data only.
  '--Secrets:MasterKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=',
];

export default defineConfig({
  testDir: './e2e',
  outputDir: './test-results',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 1 : 0,
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report' }]],
  use: {
    baseURL: `http://127.0.0.1:${port}`,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 800 } } }],
  webServer: {
    command: `dotnet run --project ../AutoCanvas.Server -c Release --no-launch-profile -- ${serverArguments.join(' ')}`,
    url: `http://127.0.0.1:${port}/health/ready`,
    timeout: 180_000,
    reuseExistingServer: false,
    stdout: 'ignore',
    stderr: 'pipe',
  },
});
