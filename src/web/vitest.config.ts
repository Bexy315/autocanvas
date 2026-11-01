import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    restoreMocks: true,
    coverage: {
      provider: 'v8',
      include: ['src/**/*.{ts,tsx}'],
      // Generated types, the entry point that only mounts the app, and test helpers.
      exclude: ['src/api/schema.d.ts', 'src/main.tsx', 'src/test/**', 'src/**/*.test.{ts,tsx}'],
      reporter: ['text-summary', 'html', 'cobertura'],
      reportsDirectory: './coverage',
      thresholds: { lines: 75 },
    },
  },
});
