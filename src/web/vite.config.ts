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
        // The editor's YAML and canvas libraries form groups of their own, imported only by the
        // editor tab, so they load when the editor is first opened. Whole packages only, with the
        // runtime in a chunk of its own: narrower groups once broke the origin's production build.
        codeSplitting: {
          groups: [
            {
              name: 'yaml-editor',
              test: /node_modules[\\/](@codemirror|@lezer|crelt|style-mod|w3c-keyname|@marijn)[\\/]/,
              priority: 3,
            },
            {
              name: 'canvas',
              test: /node_modules[\\/](@xyflow|@dagrejs|d3-[a-z]+|zustand|classcat|use-sync-external-store|yaml)[\\/]/,
              priority: 2,
            },
            { name: 'vendor', test: /node_modules[\\/]/, priority: 1 },
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
