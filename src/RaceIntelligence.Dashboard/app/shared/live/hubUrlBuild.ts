/**
 * Resolving the hub's address, at the one moment it can be resolved.
 *
 * Kept in its own module because it is imported by `vite.config.ts` and `vitest.config.ts`, which
 * run in Node before any application code exists. Nothing here may import from the app.
 *
 * **Why build time and not request time.** The browser has no environment to read, so the address
 * has to reach it somehow, and the two honest options are baking it into the bundle or shipping it
 * down with the document. Baking it in is chosen because it keeps the live routes free of a
 * server round trip they would otherwise have to make before opening a socket — and those routes
 * are client-only by design, so there is no server render to piggyback on. The cost is that
 * repointing a deployed dashboard at a different hub is a rebuild, not a restart. That is
 * acceptable for a service whose hub address changes about as often as its own does.
 *
 * `npm run dev` under AppHost picks the value up for free: Aspire injects `HUB_URL` into the
 * process environment, and Vite reads it here at config time.
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
export function resolveHubUrlAtBuildTime(raw: string | undefined): string {
  const trimmed = raw?.trim();
  if (trimmed === undefined || trimmed === '') {
    return DEFAULT_HUB_URL;
  }

  return trimmed.replace(/\/+$/, '');
}
