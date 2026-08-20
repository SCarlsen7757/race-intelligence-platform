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
/** Where one widget sits, in grid cells. */
export interface WidgetGeometry {
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface WallWidget extends WidgetGeometry {
  /** Unique per placement, so the same widget can be on the wall more than once. */
  instanceId: string;
  /** A catalogue entry's id. Resolved through `findPanel`, which may not find it. */
  widgetId: string;
  /**
   * Where this widget sits on monitors other than the one the wall is written for.
   *
   * **The `x`/`y`/`w`/`h` above are the canonical arrangement**, in the twelve columns every
   * catalogue `defaultSize` is expressed in. A narrower wall has fewer columns, so the same
   * arrangement cannot be the same numbers — and the grid, left to itself, rescales them and then
   * hands the rescaled version back, which used to be written straight over the canonical ones.
   * Opening the wall on a laptop once permanently squashed it into two thirds of the 4K screen it
   * was arranged on, with no way back.
   *
   * Keyed by breakpoint name, and absent until the user has actually arranged something at that
   * width — an untouched breakpoint is derived by the grid, which is the better first answer than
   * anything stored here would be. The canonical numbers are what an exported file leads with and
   * what a person hand-editing one would expect to edit, which is why this is a side table rather
   * than a replacement for them.
   */
  at?: Readonly<Record<string, WidgetGeometry>>;
  /**
   * Channel ids this placement has turned off. Absent means every channel is drawn.
   *
   * **Hidden rather than visible, and that is the whole of why this defaults correctly.** A wall
   * saved before channels existed carries nothing here and shows everything, which is what its
   * author saw when they saved it. The same holds when a widget *gains* a channel in a later build:
   * a list of what to hide leaves the new line drawn, where a list of what to show would silently
   * suppress it and give no clue why. The absent case being the permissive one is what makes both
   * work without a migration.
   *
   * Per placement, so two tyre-wear tiles can watch different corners — see `ChannelPanelProps`.
   */
  hiddenChannels?: readonly string[];
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
/**
 * Whether a value carries the four numbers a placement is.
 *
 * Returns a plain boolean rather than a type predicate on purpose. A predicate here narrows the
 * *widget* being checked to a bare `WidgetGeometry` for the rest of the expression, and the fields
 * checked after it — `at`, `hiddenChannels` — then stop existing on it.
 */
function isGeometry(value: unknown): boolean {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const item = value as Partial<WidgetGeometry>;
  return (
    typeof item.x === 'number' &&
    typeof item.y === 'number' &&
    typeof item.w === 'number' &&
    typeof item.h === 'number'
  );
}

export function isWallWidget(value: unknown): value is WallWidget {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const item = value as Partial<WallWidget>;
  if (!isGeometry(item)) {
    return false;
  }

  return (
    typeof item.instanceId === 'string' &&
    typeof item.widgetId === 'string' &&
    // Absent until the user arranges the wall at a second width. Values are checked but breakpoint
    // *names* are not, because they belong to the grid rather than to this document: a file naming
    // a breakpoint this build no longer has is a placement for a monitor nobody is using, which is
    // worth ignoring rather than refusing a whole wall over.
    (item.at === undefined ||
      (typeof item.at === 'object' &&
        item.at !== null &&
        Object.values(item.at).every(isGeometry))) &&
    // Absent is the ordinary case, not a defect: every wall saved before channels had toggles, and
    // every widget that draws only one thing, has nothing to say here.
    (item.hiddenChannels === undefined ||
      (Array.isArray(item.hiddenChannels) &&
        item.hiddenChannels.every((id) => typeof id === 'string')))
  );
}

/**
 * One position, coerced into a position the grid can actually place.
 *
 * `isWallWidget` proves the four fields are numbers and stops there, which was never enough: a
 * hand-edited or corrupted file can carry `w: 9999`, a negative `x`, a fractional `h`, or `NaN` —
 * `typeof NaN === 'number'` — and every one of those reaches the grid as a position. The file is
 * *meant* to be hand-edited and shared, so malformed geometry is a normal input rather than an
 * attack, and it should cost the user a moved tile rather than a wall they cannot repair.
 *
 * Deliberately knows nothing about the catalogue or the column count. Those live in `sims/`, and
 * `shared/` reaching into a feature is the dependency inversion this directory exists to avoid —
 * the same reason `readViewFile` is handed a `knowsWidget` predicate rather than importing one. The
 * grid clamps to its own columns and to each widget's `minSize`; this is the floor underneath that,
 * and it is the half that can be stated without asking anybody.
 */
function clampGeometry(geometry: WidgetGeometry): WidgetGeometry {
  const whole = (value: number, lowest: number) =>
    Number.isFinite(value) ? Math.max(lowest, Math.round(value)) : lowest;

  return {
    x: whole(geometry.x, 0),
    y: whole(geometry.y, 0),
    // One cell, not zero: a widget with no width is a widget that is on the wall and cannot be
    // seen or grabbed, which is the state this whole function exists to make unreachable.
    w: whole(geometry.w, 1),
    h: whole(geometry.h, 1),
  };
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
  const at = Object.entries(widget.at ?? {}).map(
    ([name, geometry]) => [name, clampGeometry(geometry)] as const,
  );

  return {
    instanceId: widget.instanceId,
    widgetId: widget.widgetId,
    ...clampGeometry(widget),
    ...(at.length === 0 ? {} : { at: Object.fromEntries(at) }),
    // Spread rather than assigned, because `exactOptionalPropertyTypes` distinguishes an absent
    // property from one present and undefined. An empty list is dropped too: "nothing hidden" and
    // "no opinion" are the same wall, and writing `[]` into every widget on every save would put a
    // field in the document for the majority of tiles that never toggle anything.
    ...(widget.hiddenChannels === undefined || widget.hiddenChannels.length === 0
      ? {}
      : { hiddenChannels: [...widget.hiddenChannels] }),
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
