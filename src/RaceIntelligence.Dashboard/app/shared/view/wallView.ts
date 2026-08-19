/**
 * The pit wall's saved layout, and the only place in this app that touches browser storage.
 *
 * One module with one key shape, set deliberately as a pattern rather than as convenience. Storage
 * calls scattered through components are how a key gets spelled two ways, how a parse without a
 * guard takes a page down on the one profile holding a document from an older build, and how
 * nobody can answer "what do we persist" without grepping. Everything the wall remembers goes
 * through here.
 */

/**
 * One widget on the wall: what it is, and where it sits.
 *
 * **Nothing here names a car, in any form** — not a key, not a slot, not an index. A widget shows
 * whichever driver is selected, so the wall is purely an arrangement.
 *
 * That is worth more than it first looks. Earlier drafts of this document carried a binding, and
 * every one of them had the same failure waiting in it: a saved position resolving against a
 * different session puts one driver's numbers under another driver's heading, which is the single
 * mistake a race engineer has no way to catch from the screen. With no binding to resolve, the
 * mistake cannot be constructed. It is also what makes a wall portable without qualification — the
 * same file opens in any session, of any simulator, for anybody.
 */
export interface WallWidget {
  /** Unique per placement, so the same widget can be on the wall more than once. */
  instanceId: string;
  /** A catalogue entry's id. Resolved through `findPanel`, which may not find it. */
  widgetId: string;
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
 * profile — or, once exported, on somebody's disk — and genuinely does arrive from the past.
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

/**
 * Whether one entry in a saved wall is one we can place.
 *
 * Split out so the import in `viewFile.ts` checks a widget by exactly the rule storage does. A file
 * and a storage value are the same document arriving by two routes, and two validators that agreed
 * today would be one bug away from disagreeing about which walls are loadable depending on where
 * they came from.
 */
export function isWallWidget(value: unknown): value is WallWidget {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const item = value as Partial<WallWidget>;
  return (
    typeof item.instanceId === 'string' &&
    typeof item.widgetId === 'string' &&
    typeof item.x === 'number' &&
    typeof item.y === 'number' &&
    typeof item.w === 'number' &&
    typeof item.h === 'number'
  );
}

/**
 * One widget reduced to exactly the fields this build knows.
 *
 * Documents written by earlier builds carry a binding — `driverOrdinal`, then `'selected'` and
 * `{ slot }` — and {@link isWallWidget} accepts them, because they are otherwise perfectly good
 * placements and refusing them would throw away somebody's arrangement over a field that no longer
 * matters. Copying the known fields rather than passing the object through is what stops the dead
 * binding riding along and being written back out on the next save, which would leave exported
 * files quietly carrying a reference to a car forever.
 */
export function normaliseWidget(widget: WallWidget): WallWidget {
  return {
    instanceId: widget.instanceId,
    widgetId: widget.widgetId,
    x: widget.x,
    y: widget.y,
    w: widget.w,
    h: widget.h,
  };
}

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

  return candidate.widgets.every(isWallWidget);
}

/**
 * The wall saved for one simulator, or null when this browser has never saved one.
 *
 * **Null and an empty wall are different answers**, which is why this does not collapse both into
 * a wall with no widgets. A user who has cleared every tile off the wall has said something, and
 * seeding the default arrangement back over the top of that would be the dashboard arguing with
 * them. Null means nobody has ever expressed a preference here, which is the only moment a default
 * is welcome.
 *
 * Never throws. Storage is unavailable altogether in a private window on some browsers, and the
 * value in it may be anything — neither is a reason to fail to render a session.
 */
export function loadWallView(gameKey: string): WallView | null {
  if (gameKey === '') {
    return null;
  }

  try {
    const raw = window.localStorage.getItem(wallViewKey(gameKey));
    if (raw === null) {
      return null;
    }

    const parsed: unknown = JSON.parse(raw);
    if (!isWallView(parsed)) {
      return null;
    }

    return { version: WALL_VIEW_VERSION, widgets: parsed.widgets.map(normaliseWidget) };
  } catch {
    return null;
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
