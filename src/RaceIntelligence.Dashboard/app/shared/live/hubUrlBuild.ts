/**
 * Resolving the hub's address, at the one moment it can be resolved.
 *
 * Kept in its own module because it is imported by `vite.config.ts` and `vitest.config.ts`, which
 * run in Node before any application code exists. Nothing here may import from the app.
 *
 * **This used to be the whole mechanism, and is now only the development half.** The browser has
 * no environment to read, so the address has to reach it somehow: either baked into the bundle or
 * shipped down with the document. It was baked in, which made a built image carry one deployment's
 * hostnames and be useless to anyone else. Production now ships it down — see
 * `app/shared/config/runtimeConfig.ts` and `server.mjs`.
 *
 * What is left here is normalisation, and the default that makes `npm run dev` work with no
 * configuration at all. Vite still substitutes the result, and that substituted value is what the
 * accessors fall back to when nothing was injected — which is exactly the dev server, since
 * `server.mjs` is a production entry point. AppHost's injected `HUB_URL` therefore keeps reaching a
 * `npm run dev` session for free.
 */

/**
 * The hub the dashboard talks to when nothing says otherwise.
 *
 * Matches the `http` launch profile in `src/RaceIntelligence.Web/Properties/launchSettings.json`,
 * so a developer running `dotnet run --project src/RaceIntelligence.Web --launch-profile http` and
 * `npm run dev` in two terminals needs to configure nothing at all.
 */
export const DEFAULT_HUB_URL = 'http://localhost:5044';

/**
 * Normalises whatever `HUB_URL` was set to into an origin with no trailing slash.
 *
 * Trailing slashes are stripped rather than tolerated because every use appends a path, and
 * `http://host//live/view` is a different request from `http://host/live/view` — one the hub
 * answers with a 404 that looks nothing like a configuration mistake.
 */
export function resolveHubUrl(raw: string | undefined): string {
  const trimmed = raw?.trim();
  if (trimmed === undefined || trimmed === '') {
    return DEFAULT_HUB_URL;
  }

  return trimmed.replace(/\/+$/, '');
}
