import { tanstackStart } from '@tanstack/react-start/plugin/vite';
import viteReact from '@vitejs/plugin-react';
import { defineConfig } from 'vite';
import { resolveReadUrl } from './app/shared/history/readUrlBuild';
import { resolveHubUrl } from './app/shared/live/hubUrlBuild';

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

  // These are the DEVELOPMENT source of the two origins, not the deployed one. A built image gets
  // them at run time, injected into the document by server.mjs, so that one image works for any
  // deployment. But `npm run dev` runs this dev server rather than server.mjs, so nothing injects
  // anything — the accessors fall back to what is substituted here, and AppHost's injected HUB_URL
  // reaches a dev session for free. A production build still substitutes these; nothing ever reads
  // them, because server.mjs refuses to start without the real values.
  define: {
    __HUB_URL__: JSON.stringify(resolveHubUrl(process.env.HUB_URL)),
    // The read API's address, baked in for the same reason and by the same mechanism. Two
    // addresses because history and live are two services — see readUrlBuild.ts.
    __READ_URL__: JSON.stringify(resolveReadUrl(process.env.READ_URL)),
  },

  // `PORT` because that is what Aspire assigns a JavaScript resource, and Vite does not read it on
  // its own. 3000 otherwise, which is what `docs/development.md` and the hub's development
  // `Live:AllowedOrigins` both assume for the two-terminal loop.
  server: {
    port: Number(process.env.PORT ?? 3000),
  },
});
