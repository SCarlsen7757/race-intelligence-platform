/**
 * The pit wall as a file: what gets written out, and what is made of one coming back in.
 *
 * Beside `wallView.ts` and for the same reason it is one module — this is the only place in the app
 * that hands the browser a download, exactly as that one is the only place that touches storage.
 *
 * A wall someone arranged over a race weekend is worth more than one browser profile. It should
 * survive a cleared profile, move to the machine in the garage, and be sendable to whoever wants to
 * see how you read a stint. So the wall is also a document.
 */

import {
  isWallWidget,
  normaliseWidget,
  WALL_VIEW_VERSION,
  type WallView,
  type WallWidget,
} from './wallView';

/**
 * A wall, written down.
 *
 * The stored wall plus the two things a file needs that a storage key does not: which simulator it
 * was arranged for, since the key carried that and a file has nowhere to put it, and a name, since
 * anyone keeping more than one will want to tell them apart.
 *
 * **`version` is the one version number in this codebase that means what version numbers usually
 * mean, and it is worth being explicit about why.** The project's rule is that schema versions do
 * not exist before v1.0.0 — no bumps, no migration shims, no tolerant readers — and that rule is
 * right for what it governs: the live wire and the ingest contract are spoken between processes
 * built from the same commit, so an older client is not a thing that can exist, and a ladder of
 * versions would document a fiction. None of that holds here. This document is written to a disk,
 * emailed to somebody, and opened months later by a build that has never met it. It is the one
 * artefact in the system that genuinely does arrive from the past, which is why it says what it is.
 */
export interface ViewFile {
  version: number;
  /** What the user called this wall. Absent on a file written before there was a way to name one. */
  name?: string;
  /** The simulator it was arranged for. Advisory — see `readViewFile`. */
  gameKey: string;
  widgets: WallWidget[];
}

/** What a file is called when it lands in someone's downloads folder. */
export function viewFileName(gameKey: string): string {
  return `pitwall-${gameKey}.json`;
}

/**
 * The wall, as the text of a file.
 *
 * Indented, because this is a document a person may open, diff, or hand-edit — and the few hundred
 * bytes that costs are not worth the minified alternative for a file this size.
 */
export function serialiseViewFile(gameKey: string, view: WallView, name?: string): string {
  const file: ViewFile = {
    version: WALL_VIEW_VERSION,
    // Spread rather than assigned: `exactOptionalPropertyTypes` distinguishes an absent property
    // from one present and undefined, and an unnamed wall should have no key rather than a null one.
    ...(name === undefined || name === '' ? {} : { name }),
    gameKey,
    // Normalised on the way out as well as on the way in. A wall held in memory came from somewhere
    // — storage, an import, a default — and this is the last point at which a field nobody reads any
    // more could be written into a document that outlives the build. A file leaving here names
    // widgets and positions, and nothing else.
    widgets: view.widgets.map(normaliseWidget),
  };

  return `${JSON.stringify(file, null, 2)}\n`;
}

/**
 * Hands the file to the browser.
 *
 * An object URL revoked immediately after the click: the anchor has already been followed by the
 * time the handler returns, and leaving it alive pins the blob in memory for the life of the
 * document. The anchor is never in the tree — appending it would be visible for a frame on some
 * browsers and it has no reason to be part of the page.
 */
export function downloadViewFile(gameKey: string, view: WallView, name?: string): void {
  const blob = new Blob([serialiseViewFile(gameKey, view, name)], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');

  anchor.href = url;
  anchor.download = viewFileName(gameKey);
  anchor.click();

  URL.revokeObjectURL(url);
}

/**
 * What reading a file produced.
 *
 * A refusal carries a reason rather than a code, because there is exactly one place it is shown and
 * the reason is the whole of what the user needs. A success carries the widgets it could not keep,
 * so the wall can name them instead of quietly being shorter than the file was.
 */
export type ViewFileRead =
  | {
      ok: true;
      view: WallView;
      /** Ids this build has never heard of, dropped. Named to the user, never silently discarded. */
      dropped: string[];
      /**
       * The simulator the file says it was arranged for, or null when it does not say.
       *
       * Reported rather than judged. Whether this counts as a mismatch is the caller's question,
       * because only the caller knows which room it is importing into.
       */
      gameKey: string | null;
      name: string | null;
    }
  | { ok: false; reason: string };

/**
 * Turns the text of a file into a wall, or into a reason it cannot be one.
 *
 * Three things can be wrong with a file that is otherwise fine, and they get three different
 * answers, because treating them alike is how an import quietly rearranges someone's work:
 *
 * - **A widget id this build has never heard of** — a file from a newer build, or a typo. There is
 *   nothing to render and nothing to wait for, so it is dropped, and it is named. A wall that comes
 *   back shorter with no explanation is the failure this avoids.
 * - **A widget this build knows but this session cannot feed** — a brake chart in a room whose
 *   collector reports no brakes. **Kept**, placed, and left to say so on the wall. It is not broken,
 *   it is waiting, and it comes back the moment a collector that reports the channel joins. This is
 *   not handled here at all: the widget is kept and `PitWall` already says why, which is the point —
 *   the capability question belongs to the session, not to the file.
 * - **A file from another simulator** — offered, not refused. Nearly every widget is gated on a
 *   capability rather than on a game, so a RaceRoom wall is mostly meaningful against another sim's
 *   session, and the parts that are not are already covered by the case above. The mismatch is
 *   reported so the user knows what they opened.
 *
 * Anything genuinely unreadable is refused, and the caller leaves the wall alone. `knowsWidget` is
 * injected rather than imported so this module stays clear of the catalogue, which lives under
 * `sims/` — `shared` reaching into a feature is the dependency inversion the rest of this directory
 * is arranged to avoid.
 */
export function readViewFile(
  text: string,
  knowsWidget: (widgetId: string) => boolean,
): ViewFileRead {
  let parsed: unknown;

  try {
    parsed = JSON.parse(text);
  } catch {
    return { ok: false, reason: 'That file is not JSON.' };
  }

  if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
    return { ok: false, reason: 'That file does not contain a saved wall.' };
  }

  const candidate = parsed as Partial<ViewFile>;

  if (!Array.isArray(candidate.widgets)) {
    return { ok: false, reason: 'That file does not contain a saved wall.' };
  }

  // Checked after the shape, so a JSON file that happens to carry a version gets told it is not a
  // wall rather than being told its version is wrong.
  if (candidate.version !== WALL_VIEW_VERSION) {
    return {
      ok: false,
      reason:
        typeof candidate.version === 'number'
          ? `That wall was saved in format version ${candidate.version}, and this build reads version ${WALL_VIEW_VERSION}.`
          : 'That file does not say which format it is in.',
    };
  }

  if (!candidate.widgets.every(isWallWidget)) {
    return { ok: false, reason: 'That wall has a widget this build cannot read.' };
  }

  const widgets: WallWidget[] = candidate.widgets;
  const kept = widgets.filter((widget) => knowsWidget(widget.widgetId)).map(normaliseWidget);

  // Deduplicated, because a wall with the same unknown widget placed four times is one thing the
  // user needs to be told, not four.
  const dropped = [
    ...new Set(
      widgets.filter((widget) => !knowsWidget(widget.widgetId)).map((widget) => widget.widgetId),
    ),
  ];

  return {
    ok: true,
    view: { version: WALL_VIEW_VERSION, widgets: kept },
    dropped,
    gameKey: typeof candidate.gameKey === 'string' ? candidate.gameKey : null,
    name: typeof candidate.name === 'string' ? candidate.name : null,
  };
}
