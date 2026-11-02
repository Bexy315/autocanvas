import { fileURLToPath } from 'node:url';
import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// The server this dev server sends API calls to: `dotnet run` of AutoCanvas.Server by default.
const server = process.env['AUTOCANVAS_SERVER_URL'] ?? 'http://localhost:5080';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  build: {
    // The server serves the UI from its web root (ADR-0018); that directory is not committed.
    outDir: fileURLToPath(new URL('../AutoCanvas.Server/wwwroot', import.meta.url)),
    emptyOutDir: true,
    assetsDir: 'assets',
    rolldownOptions: {
      output: {
        // Libraries apart from the app: they change less often, so a new release leaves them cached.
        // One group for all of node_modules (narrower groups once broke the origin's production
        // build), except the editor's libraries, which load with the editor when it is opened.
        codeSplitting: {
          groups: [
            {
              name: 'vendor',
              test: /node_modules[\\/](?!(@codemirror|@lezer|crelt|style-mod|w3c-keyname|@marijn|yaml)[\\/])/,
              priority: 1,
            },
          ],
        },
      },
    },
  },
  server: {
    port: 5173,
    strictPort: true,
    // The editor completes keys from the automation schema, which lives at the repository root.
    fs: { allow: ['.', fileURLToPath(new URL('../../schema', import.meta.url))] },
    // Same origin for the browser, so the session cookie and the CSRF header work as in production.
    proxy: { '/api': { target: server, changeOrigin: false } },
  },
});
