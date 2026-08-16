import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

/**
 * The dashboard is served by the hub in production, so it builds straight into the host's
 * wwwroot rather than a directory something else has to copy. One less step to forget, and
 * `dotnet run` after a `npm run build` serves exactly what CI would publish.
 *
 * In development the Vite dev server runs instead, for hot reload, and proxies the two paths the
 * app talks to back to the hub. That keeps the client's own code identical in both modes: it
 * always opens its socket against its own origin, and never needs a configured API address.
 */
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true,
    // Live traces are the point of this app, so the chart library is worth splitting out: it
    // changes far less often than the dashboard does, and a separate chunk keeps it cached across
    // deploys.
    rollupOptions: {
      output: {
        manualChunks: {
          uplot: ['uplot'],
        },
      },
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5044', changeOrigin: true },
      // ws:true is what makes the live socket work behind the dev server. Without it the upgrade
      // request is proxied as a plain GET and the connection fails with a 400 that looks like a
      // server bug rather than a proxy misconfiguration.
      '/live': { target: 'http://localhost:5044', ws: true, changeOrigin: true },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./vitest.setup.ts'],
  },
});
