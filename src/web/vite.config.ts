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
  },
  server: {
    port: 5173,
    strictPort: true,
    // Same origin for the browser, so the session cookie and the CSRF header work as in production.
    proxy: { '/api': { target: server, changeOrigin: false } },
  },
});
