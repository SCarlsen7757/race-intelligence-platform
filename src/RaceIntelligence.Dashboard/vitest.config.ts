import viteReact from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';
import { DEFAULT_READ_URL } from './app/shared/history/readUrlBuild';
import { DEFAULT_HUB_URL } from './app/shared/live/hubUrlBuild';

/**
 * Separate from `vite.config.ts`, and deliberately without the TanStack Start plugin.
 *
 * The tests exercise components, the live store and the router's URL handling; none of that needs
 * the server build, the route-tree code generation or the SSR environments the Start plugin sets
 * up. Loading it here would make every unit test depend on a generated file being current, which
 * is a slow and confusing way to fail.
 */
export default defineConfig({
  plugins: [viteReact()],

  // Pinned to the default rather than read from the environment: a test asserting which address
  // the socket opens against must not answer differently on a machine that happens to have
  // HUB_URL exported.
  define: {
    __HUB_URL__: JSON.stringify(DEFAULT_HUB_URL),
    __READ_URL__: JSON.stringify(DEFAULT_READ_URL),
  },

  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./vitest.setup.ts'],
  },
});
