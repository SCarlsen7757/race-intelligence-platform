/**
 * Resolving the read API's address, at the one moment it can be resolved.
 *
 * The exact counterpart of `shared/live/hubUrlBuild.ts`, and separate from it for the same reason
 * that file is separate: this is imported by `vite.config.ts` and `vitest.config.ts`, which run in
 * Node before any application code exists. Nothing here may import from the app.
 *
 * **Why there are two addresses now.** History and live come from two different services, and not
 * incidentally — the hub holds no database credentials at all, and the read API holds no live
 * state. Neither could serve the other's data without becoming the thing the split avoids. So the
 * dashboard is told where both are.
 */

/**
 * The read API the dashboard talks to when nothing says otherwise.
 *
 * Matches the `http` launch profile in
 * `src/RaceIntelligence.Read.RaceRoom/Properties/launchSettings.json`, so the two-terminal
 * development loop needs no configuration.
 */
export const DEFAULT_READ_URL = 'http://localhost:5049';

/**
 * Normalises whatever `READ_URL` was set to into an origin with no trailing slash.
 *
 * Trailing slashes are stripped rather than tolerated because every use appends a path, and
 * `http://host//api/v1/sessions` is a different request from the one intended — answered with a
 * 404 that looks nothing like a configuration mistake.
 */
export function resolveReadUrl(raw: string | undefined): string {
  const trimmed = raw?.trim();
  if (trimmed === undefined || trimmed === '') {
    return DEFAULT_READ_URL;
  }

  return trimmed.replace(/\/+$/, '');
}
