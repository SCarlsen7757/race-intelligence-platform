import { tanstackStart } from '@tanstack/react-start/plugin/vite';
import viteReact from '@vitejs/plugin-react';
import { defineConfig } from 'vite';
import { resolveHubUrlAtBuildTime } from './app/shared/live/hubUrlBuild';

/**
 * The dashboard is its own service now: a Node process on its own origin, talking to the hub
 * across the network rather than being served by it. That is why there is no `outDir` pointing
 * into the hub's `wwwroot` any more, and no dev-server proxy — the browser opens its socket
 * straight at the hub, which is both the more modular arrangement and the lower-latency one. A
 * proxy would add a second hop and force this event loop to re-emit every focus frame sixty times
 * a second.
 *
 * `srcDirectory: 'app'` so the tree matches the feature layout the app is organised by:
 * `app/routes` for the URL surface, `app/features` for what each route shows, `app/shared` for the
 * live client every route uses.
 */
export default defineConfig({
  plugins: [tanstackStart({ srcDirectory: 'app' }), viteReact()],

  // The hub's address is baked in here rather than read at runtime in the browser, because the
  // browser has no environment to read. `HUB_URL` is taken from whatever launched this process —
  // AppHost injects the hub's resolved endpoint — so `npm run dev` under AppHost needs no
  // configuration, and a production build takes it from the deploying environment. See
  // hubUrlBuild.ts for why the fallback is what it is.
  define: {
    __HUB_URL__: JSON.stringify(resolveHubUrlAtBuildTime(process.env.HUB_URL)),
  },

  // `PORT` because that is what Aspire assigns a JavaScript resource, and Vite does not read it on
  // its own. 3000 otherwise, which is what `docs/development.md` and the hub's development
  // `Live:AllowedOrigins` both assume for the two-terminal loop.
  server: {
    port: Number(process.env.PORT ?? 3000),
  },
});
