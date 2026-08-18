/**
 * The pit wall's saved layout, and the only place in this app that touches browser storage.
 *
 * One module with one key shape, set deliberately as a pattern rather than as convenience. Storage
 * calls scattered through components are how a key gets spelled two ways, how a parse without a
 * guard takes a page down on the one profile holding a document from an older build, and how
 * nobody can answer "what do we persist" without grepping. Everything the wall remembers goes
 * through here.
 */

import { isDriverBinding, type WallDriverBinding } from './driverBinding';

/** One widget on the wall: what it is, whose car it is about, and where it sits. */
export interface WallWidget {
  /** Unique per placement, so the same widget can be on the wall twice for two different cars. */
  instanceId: string;
  /** A catalogue entry's id. Resolved through `findPanel`, which may not find it. */
  widgetId: string;
  /**
   * Which car this tile is about, by position rather than by key — see {@link WallDriverBinding}
   * for why a key must never be written here. Absent for a widget that is about the room.
   */
  driver?: WallDriverBinding;
  x: number;
  y: number;
  w: number;
  h: number;
}

/**
 * A saved wall.
 *
 * `version` describes this document rather than any wire contract, and it is the one versioned
 * thing in this codebase that the project's no-backward-compatibility rule does not cover: a wire
 * message is built and consumed by processes from the same commit, whereas this sits in a browser
 * profile and genuinely does arrive from the past. It is read here and will be read again by the
 * export format in #56.
 */
export interface WallView {
  version: 1;
  widgets: WallWidget[];
}

export const WALL_VIEW_VERSION = 1;

/**
 * Where one simulator's wall is kept.
 *
 * Keyed by game key, not by room. Which widgets are worth having in front of you is decided by what
 * the simulator can report and by how you engineer that car — not by which session you happen to
 * have open — so two rooms of the same sim open the same wall, and a wall arranged during practice
 * is still there for the race.
 */
export function wallViewKey(gameKey: string): string {
  return `pitwall:view:${gameKey}`;
}

export const EMPTY_WALL: WallView = { version: WALL_VIEW_VERSION, widgets: [] };

/**
 * Whether a value parsed out of storage is a wall we can use.
 *
 * Structural rather than trusting the version number alone. A document can carry the right version
 * and still be truncated, hand-edited, or written by a build whose widget shape has since changed,
 * and the wall would rather start empty than throw during render.
 */
function isWallView(value: unknown): value is WallView {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const candidate = value as Partial<WallView>;
  if (candidate.version !== WALL_VIEW_VERSION || !Array.isArray(candidate.widgets)) {
    return false;
  }

  return candidate.widgets.every((widget) => {
    if (typeof widget !== 'object' || widget === null) {
      return false;
    }

    const item = widget as Partial<WallWidget>;
    return (
      typeof item.instanceId === 'string' &&
      typeof item.widgetId === 'string' &&
      typeof item.x === 'number' &&
      typeof item.y === 'number' &&
      typeof item.w === 'number' &&
      typeof item.h === 'number' &&
      (item.driver === undefined || isDriverBinding(item.driver))
    );
  });
}

/**
 * The wall saved for one simulator, or an empty one.
 *
 * Never throws. Storage is unavailable altogether in a private window on some browsers, and the
 * value in it may be anything — neither is a reason to fail to render a session.
 */
export function loadWallView(gameKey: string): WallView {
  if (gameKey === '') {
    return EMPTY_WALL;
  }

  try {
    const raw = window.localStorage.getItem(wallViewKey(gameKey));
    if (raw === null) {
      return EMPTY_WALL;
    }

    const parsed: unknown = JSON.parse(raw);
    return isWallView(parsed) ? parsed : EMPTY_WALL;
  } catch {
    return EMPTY_WALL;
  }
}

/**
 * Saves one simulator's wall.
 *
 * Silent on failure, for the same reason loading is: a full or unavailable storage quota should
 * cost the user their saved arrangement, not their session.
 */
export function saveWallView(gameKey: string, view: WallView): void {
  if (gameKey === '') {
    return;
  }

  try {
    window.localStorage.setItem(wallViewKey(gameKey), JSON.stringify(view));
  } catch {
    // Nothing to do and nothing worth saying. See the remark above.
  }
}
