import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Responsive, useContainerWidth, type Layout } from 'react-grid-layout';
import 'react-grid-layout/css/styles.css';
import { useAllExtras, useLive } from '../../shared/live/useLive';
import { downloadViewFile, readViewFile } from '../../shared/view/viewFile';
import {
  loadWallView,
  saveWallView,
  WALL_VIEW_VERSION,
  type WallView,
  type WallWidget,
  type WidgetGeometry,
} from '../../shared/view/wallView';
import {
  defaultWallFor,
  findPanel,
  hasChannels,
  isDriverWidget,
  panelsFor,
} from '../../sims/registry';
import {
  BREAKPOINTS,
  breakpointFor,
  CANONICAL_BREAKPOINT,
  COLUMNS,
  WALL_BREAKPOINTS,
  type WallBreakpoint,
} from './breakpoints';
import { fitToGrid, fitWidget, geometryAt, withGeometryAt } from './geometry';
import '../../sims/raceroom';

interface PitWallProps {
  gameKey: string;
  capabilities: readonly string[];
  /**
   * The car every widget on this wall is about.
   *
   * One car, and required rather than nullable: the wall is only mounted once something is
   * selected, because a wall with nobody to draw is not a wall with empty tiles — it is a page that
   * should be showing the timing tower instead. See `SessionView` for the two states.
   */
  driverKey: string;
  /** How the car is named in the wall's heading. */
  displayName: (driverKey: string) => string;
}

/**
 * How tall one grid cell is, in pixels.
 *
 * A chart's minimum useful height is what set this: the smallest thing on the wall asks for two
 * rows, and a uPlot trace stops being readable below about a hundred pixels of plot area.
 */
const ROW_HEIGHT = 34;

/**
 * The tile's heading is the only thing that starts a drag.
 *
 * Without a handle every chart inside a tile swallows the gesture, and a uPlot canvas is most of a
 * tile's surface — so a whole-tile drag would feel broken everywhere it matters. Hoisted to module
 * scope so it is not a fresh object on every render, for the same reason the chart specs are.
 */
const DRAG_CONFIG = { handle: '.wall__widget-grip' } as const;

/**
 * Stands in for a tile that has hidden nothing, hoisted so it is the same array every render.
 *
 * A fresh `[]` per render would re-run `LiveChart`'s visibility effect on every layout change, and
 * that effect forces a repaint — so a drag anywhere on the wall would repaint every chart on it.
 */
const NO_HIDDEN_CHANNELS: readonly string[] = [];

/**
 * How long the wall waits before writing itself to storage.
 *
 * Long enough that a drag — which produces a layout callback per frame — writes once when it stops
 * rather than forty times while it is happening, and short enough that nobody notices. See the
 * effect that uses it.
 */
const SAVE_DELAY_MS = 400;

/** Enough to tell two placements of the same widget apart, which is all an instance id is for. */
function newInstanceId(): string {
  return `w${Date.now().toString(36)}${Math.random().toString(36).slice(2, 7)}`;
}

/**
 * A widget that cannot run here, saying why.
 *
 * The wall is a layout someone arranged and saved, and it is opened against sessions whose
 * collectors differ — so this is a routine state, not an error. Removing the tile instead would
 * silently rewrite their wall and leave them to work out what had gone; a tile that says what it is
 * waiting for is the difference between "this session has no brake data" and "the dashboard is
 * broken".
 */
function UnavailableWidget({ reason }: { reason: string }) {
  return <p className="wall__unavailable">{reason}</p>;
}

/**
 * What an import has to say for itself.
 *
 * Shown on the wall rather than through `ErrorBanner`, which is fed exclusively by the hub — its
 * state is a `LiveErrorMessage` carrying a `LiveErrorCode` off the socket, and it branches on those
 * codes. Pushing a local complaint through it would mean inventing a wire code for something that
 * never crossed the wire, and would put "the file you chose is not JSON" in the same place as "the
 * room closed". A message about a file belongs beside the button that read the file.
 */
interface ImportNotice {
  tone: 'info' | 'warn';
  text: string;
}

/**
 * The pit wall: the widgets the user has placed, where they placed them.
 *
 * **Nothing here renders per focus frame.** Every widget paints itself from a
 * `requestAnimationFrame` loop against the store's ring buffers. This component re-renders when the
 * layout changes — a drag, a resize, an add, a remove — and at no other time, which is why dragging
 * a tile does not compete with sixty frames a second of telemetry.
 *
 * **No widget is bound to a car.** Every tile is about whichever driver is selected, which is what
 * makes the saved arrangement portable: a wall names widgets and positions and nothing else, so the
 * same document opens against any session without a position ever resolving to the wrong car. The
 * layout is stored per simulator; see `wallView.ts` for why that is the right grain.
 */
export function PitWall({ gameKey, capabilities, driverKey, displayName }: PitWallProps) {
  const { store } = useLive();
  const { width, containerRef } = useContainerWidth();

  // A widget can report that it has nothing to say for a car — incident points on a sim that
  // reports none, for instance. Honoured here for the same reason #46 made the old strip honour it:
  // a frame around four dashes is worse than no frame, and worse still on a wall, where the user
  // put the tile there on purpose and deserves to know it is waiting rather than broken.
  const extras = useAllExtras();

  // Sorted and deduplicated: with two collectors in a room the same capability arrives twice, and
  // an unstable list would rebuild the picker on every room-list message.
  const stableCapabilities = useMemo(() => [...new Set(capabilities)].sort(), [capabilities]);

  // A wall nobody has arranged gets the simulator's suggested one; a wall somebody has emptied
  // stays empty. `loadWallView` returns null only for the first, which is the whole reason it
  // distinguishes them — see the remark there.
  //
  // Fitted on the way in, not only on the way out of a file picker. Storage is as capable of
  // holding a placement outside the grid as a file is — a wall arranged in a build with different
  // widgets, or a profile someone has edited — and a tile the user cannot reach is a tile they
  // cannot remove.
  const startingWidgets = useCallback(
    () =>
      (loadWallView(gameKey)?.widgets ?? defaultWallFor(gameKey, stableCapabilities)).map(
        (widget) => fitWidget(widget, gameKey),
      ),
    [gameKey, stableCapabilities],
  );

  const [widgets, setWidgets] = useState<WallWidget[]>(startingWidgets);
  const [picking, setPicking] = useState(false);
  const [notice, setNotice] = useState<ImportNotice | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  // Reloaded during render rather than from an effect, which is React's advice for state derived
  // from a prop: an effect would paint one frame of the previous simulator's wall against this
  // session before correcting itself.
  const [loadedGameKey, setLoadedGameKey] = useState(gameKey);
  if (loadedGameKey !== gameKey) {
    setLoadedGameKey(gameKey);
    setWidgets(startingWidgets);
    setPicking(false);
    // A complaint about a file imported into the previous simulator's wall says nothing about this
    // one, and leaving it up would attach it to a session it was never about.
    setNotice(null);
  }

  /*
   * Saved on a trailing delay, not on every layout callback.
   *
   * The grid fires `onLayoutChange` continuously through a drag rather than once when it ends, so
   * this effect used to serialise the whole view and write it to `localStorage` dozens of times a
   * second while a tile was being moved. `localStorage` is synchronous and blocks the main thread —
   * the same thread every chart's paint loop runs on — so the cost landed as the traces stuttering
   * during a drag, which reads as "dragging is heavy" rather than as a storage write.
   *
   * A trailing delay rather than binding to a drag-stop event, because it covers every path that
   * changes the wall — add, remove, channel toggle, import, reset — instead of the two gestures.
   * The cleanup writes immediately, so a wall is never lost by leaving the session inside the
   * window: unmount and a simulator change both flush what was pending.
   */
  const unsaved = useRef<WallView | null>(null);

  useEffect(() => {
    unsaved.current = { version: WALL_VIEW_VERSION, widgets };

    const timer = setTimeout(() => {
      if (unsaved.current !== null) {
        saveWallView(gameKey, unsaved.current);
        unsaved.current = null;
      }
    }, SAVE_DELAY_MS);

    return () => {
      clearTimeout(timer);
    };
  }, [gameKey, widgets]);

  // Flushed when the wall goes away rather than on every change, which is what keeps the delay
  // above a delay. Keyed on the simulator so a wall is written out before the next one is loaded
  // over it, and run on unmount so leaving the session inside the window never costs an
  // arrangement.
  useEffect(
    () => () => {
      if (unsaved.current !== null) {
        saveWallView(gameKey, unsaved.current);
        unsaved.current = null;
      }
    },
    [gameKey],
  );

  /** What the picker offers: everything this room can actually feed. */
  const addable = useMemo(
    () => panelsFor(gameKey, stableCapabilities).filter(isDriverWidget),
    [gameKey, stableCapabilities],
  );

  const available = useMemo(() => new Set(addable.map((panel) => panel.id)), [addable]);

  const addWidget = useCallback(
    (widgetId: string) => {
      const entry = findPanel(gameKey, widgetId);
      if (entry === null) {
        return;
      }

      setWidgets((current) => [
        ...current,
        {
          instanceId: newInstanceId(),
          widgetId,
          // Dropped at the bottom of the wall, where there is always room. Placing it in the first
          // gap would be cleverer and worse: a widget appearing somewhere in the middle of an
          // arrangement the user built is a widget they then have to go and find.
          //
          // One row past the lowest tile rather than `Number.MAX_SAFE_INTEGER`. The grid compacts
          // either to the same place, but this is a number a person reading an exported file can
          // make sense of — and the canonical arrangement is only rewritten when somebody arranges
          // at that width, so a sentinel dropped in here would stay in the file indefinitely.
          x: 0,
          y: current.reduce((lowest, widget) => Math.max(lowest, widget.y + widget.h), 0),
          w: entry.defaultSize.w,
          h: entry.defaultSize.h,
        },
      ]);
      setPicking(false);
    },
    [gameKey],
  );

  const removeWidget = useCallback((instanceId: string) => {
    setWidgets((current) => current.filter((widget) => widget.instanceId !== instanceId));
  }, []);

  /**
   * Turns one channel off, or back on, for one tile.
   *
   * Keyed by `instanceId` rather than by widget id, which is the whole point: two tyre-wear tiles
   * are two separate questions, and narrowing one to the front left must leave the other alone. It
   * is also why this lives on the widget in the saved wall rather than anywhere central — there is
   * no such thing as "the" tyre-wear chart's channels.
   */
  const toggleChannel = useCallback((instanceId: string, channelId: string) => {
    setWidgets((current) =>
      current.map((widget) => {
        if (widget.instanceId !== instanceId) {
          return widget;
        }

        const hidden = widget.hiddenChannels ?? [];
        const next = hidden.includes(channelId)
          ? hidden.filter((id) => id !== channelId)
          : [...hidden, channelId];

        return { ...widget, hiddenChannels: next };
      }),
    );
  }, []);

  const exportView = useCallback(() => {
    downloadViewFile(gameKey, { version: WALL_VIEW_VERSION, widgets });
  }, [gameKey, widgets]);

  /**
   * Back to the simulator's suggested wall.
   *
   * The wall is otherwise a one-way door: `loadWallView` deliberately tells "never arranged" from
   * "arranged empty", so clearing every tile does not bring the default back, and there is no undo.
   * That was survivable while every arrangement was reachable — and it stopped being so the moment
   * a geometry the user did not choose could put a tile somewhere its Remove button cannot be
   * clicked. Import already refuses to leave the wall in a state it cannot get out of; this is the
   * same promise for every other route in.
   *
   * Says what it did, because a wall vanishing and being replaced is a large enough change that
   * silence would read as a fault.
   */
  const resetView = useCallback(() => {
    setWidgets(defaultWallFor(gameKey, stableCapabilities));
    setPicking(false);
    setNotice({ tone: 'info', text: 'Back to the starting wall for this simulator.' });
  }, [gameKey, stableCapabilities]);

  /**
   * Reads a chosen file onto the wall.
   *
   * The wall is replaced only on a successful read. A refusal leaves the arrangement exactly as it
   * was, which is the whole contract of this control: choosing the wrong file must never cost
   * somebody the layout they already had, because there is no undo and the file they wanted may be
   * the one they cannot find.
   */
  const importView = useCallback(
    async (file: File) => {
      const result = readViewFile(
        await file.text(),
        (widgetId) => findPanel(gameKey, widgetId) !== null,
      );

      if (!result.ok) {
        setNotice({ tone: 'warn', text: result.reason });
        return;
      }

      setWidgets(result.view.widgets.map((widget) => fitWidget(widget, gameKey)));

      // Said in one breath, because they are one event. A wall can arrive from another simulator
      // *and* carry a widget this build has never heard of, and two banners for one import would
      // read as two problems.
      const notes: string[] = [];

      if (result.gameKey !== null && result.gameKey !== gameKey) {
        notes.push(
          `This wall was saved for ${result.gameKey}. Widgets this session cannot feed will say so.`,
        );
      }

      if (result.dropped.length > 0) {
        notes.push(
          `Dropped ${result.dropped.length === 1 ? 'a widget' : 'widgets'} this build does not have: ${result.dropped.join(', ')}.`,
        );
      }

      setNotice(
        notes.length === 0
          ? { tone: 'info', text: `Loaded ${result.name ?? 'the wall'}.` }
          : { tone: 'warn', text: notes.join(' ') },
      );
    },
    [gameKey],
  );

  /**
   * Which monitor's arrangement is being edited.
   *
   * Derived from the measured width rather than tracked through the grid's `onBreakpointChange`,
   * so it cannot be a frame behind the layout callback that depends on it. See `breakpointFor`.
   */
  const breakpoint = breakpointFor(width);

  /**
   * Writes a drag or a resize back — to *this* monitor's arrangement, not to every monitor's.
   *
   * Merged into the existing widgets rather than rebuilt from the layout, because the layout only
   * carries geometry — the widget id lives here and would be lost by a rebuild.
   *
   * The breakpoint is the load-bearing part. This callback fires for the grid's own rescaling as
   * well as for a gesture, so writing its numbers into the canonical fields meant that opening the
   * wall on a narrower screen once rewrote the arrangement for every screen, permanently. Routed
   * through `withGeometryAt`, a laptop's placements land in that breakpoint's side table and the
   * numbers the 4K wall is written in are never touched.
   */
  const onLayoutChange = useCallback(
    (layout: Layout) => {
      const geometry = new Map(layout.map((item) => [item.i, item]));

      setWidgets((current) =>
        current.map((widget) => {
          const item = geometry.get(widget.instanceId);
          if (item === undefined) {
            return widget;
          }

          return withGeometryAt(widget, breakpoint, {
            x: item.x,
            y: item.y,
            w: item.w,
            h: item.h,
          });
        }),
      );
    },
    [breakpoint],
  );

  /**
   * The grid's own view of the wall, one layout per breakpoint, with each widget's floor attached.
   *
   * `minW`/`minH` come off the catalogue entry, so the size below which a widget stops being worth
   * reading is set by the widget and enforced by the grid — the drag simply stops. Capped at the
   * breakpoint's column count, because a widget asking for six columns on a four-column wall would
   * otherwise be given a floor it cannot stand on. A widget whose entry has gone missing gets no
   * floor, because there is nobody left to ask — and it still has to be movable, or it cannot be
   * removed.
   *
   * Every breakpoint is stated rather than left for the grid to derive. A derived layout is handed
   * back through `onLayoutChange` and stored anyway, so the alternative is not "fewer numbers", it
   * is "the same numbers, arrived at one render later".
   */
  const layouts = useMemo(() => {
    const build = (name: WallBreakpoint): Layout =>
      widgets.map((widget) => {
        const entry = findPanel(gameKey, widget.widgetId);
        const geometry: WidgetGeometry = fitToGrid(widget, gameKey, name, geometryAt(widget, name));

        return {
          i: widget.instanceId,
          ...geometry,
          ...(entry === null
            ? {}
            : {
                minW: Math.min(entry.minSize.w, COLUMNS[name]),
                minH: entry.minSize.h,
              }),
        };
      });

    const result: Partial<Record<WallBreakpoint, Layout>> = {};

    for (const name of WALL_BREAKPOINTS) {
      // A width nobody has arranged is deliberately left out, so the grid derives it by scaling the
      // canonical arrangement — three tiles across twelve columns become two across eight, in
      // proportion. Stating it here instead would mean clamping twelve-column numbers into eight,
      // which stacks everything into the first column and calls it a layout. The derived version is
      // handed back through `onLayoutChange` and stored, so a width becomes explicit the first time
      // it is used and is never derived again.
      if (name === CANONICAL_BREAKPOINT || widgets.some((widget) => widget.at?.[name])) {
        result[name] = build(name);
      }
    }

    return result;
  }, [gameKey, widgets]);

  return (
    <section className="wall" aria-label="Pit wall">
      <header className="wall__header">
        {/*
          The car is named once, here, rather than on every tile. Every widget on the wall is about
          the same driver, so repeating it twelve times would be twelve pieces of furniture saying
          something the heading already said.
        */}
        <h2 className="wall__title">{displayName(driverKey)}</h2>

        <button
          type="button"
          className="link-button"
          aria-expanded={picking}
          onClick={() => setPicking((open) => !open)}
        >
          {picking ? 'Cancel' : '+ Add widget'}
        </button>

        <button
          type="button"
          className="link-button"
          disabled={widgets.length === 0}
          onClick={exportView}
        >
          Export
        </button>

        {/*
          The input is the file picker; the button is what anyone can see. A bare file input cannot
          be styled to match anything around it and reads as a foreign control in a dashboard, so it
          stays hidden and the button clicks it.
        */}
        <button type="button" className="link-button" onClick={() => fileInputRef.current?.click()}>
          Import
        </button>

        <button type="button" className="link-button" onClick={resetView}>
          Reset
        </button>
        <input
          ref={fileInputRef}
          type="file"
          accept="application/json,.json"
          className="wall__file-input"
          aria-label="Import a wall"
          onChange={(event) => {
            const file = event.target.files?.[0];
            // Cleared so choosing the same file twice fires again. A picker that silently does
            // nothing the second time reads as broken, and re-importing after a bad edit is exactly
            // when somebody would try it.
            event.target.value = '';
            if (file !== undefined) {
              void importView(file);
            }
          }}
        />
      </header>

      {notice !== null && (
        <div className={`banner banner--${notice.tone}`} role="status">
          <span>{notice.text}</span>
          <button type="button" className="link-button" onClick={() => setNotice(null)}>
            Dismiss
          </button>
        </div>
      )}

      {picking && (
        <div className="wall__picker">
          {addable.length === 0 ? (
            <p className="wall__note">
              This session reports no channels that can be put on the wall.
            </p>
          ) : (
            addable.map((panel) => (
              <button
                key={panel.id}
                type="button"
                className="link-button wall__picker-item"
                onClick={() => addWidget(panel.id)}
              >
                {panel.title}
              </button>
            ))
          )}
        </div>
      )}

      <div ref={containerRef} className="wall__grid">
        {widgets.length === 0 && (
          <p className="wall__note">
            Nothing on the wall. Add the charts you want in front of you and they will be here next
            time.
          </p>
        )}

        <Responsive
          width={width}
          layouts={layouts}
          breakpoints={BREAKPOINTS}
          cols={COLUMNS}
          rowHeight={ROW_HEIGHT}
          onLayoutChange={onLayoutChange}
          dragConfig={DRAG_CONFIG}
        >
          {widgets.map((widget) => {
            const entry = findPanel(gameKey, widget.widgetId);

            return (
              <div key={widget.instanceId} className="wall__widget">
                <header className="wall__widget-head">
                  <span className="wall__widget-grip" aria-hidden="true">
                    ⠿
                  </span>
                  <h3 className="wall__widget-title">{entry?.title ?? widget.widgetId}</h3>
                  <button
                    type="button"
                    className="link-button"
                    aria-label={`Remove ${entry?.title ?? widget.widgetId}`}
                    onClick={() => removeWidget(widget.instanceId)}
                  >
                    Remove
                  </button>
                </header>

                <div className="wall__widget-body">
                  {entry === null ? (
                    <UnavailableWidget
                      reason={`This build has no widget called “${widget.widgetId}”.`}
                    />
                  ) : !available.has(entry.id) ? (
                    <UnavailableWidget
                      reason={`No collector in this session reports ${entry.title.toLowerCase()}.`}
                    />
                  ) : !isDriverWidget(entry) ? (
                    <entry.component store={store} />
                  ) : entry.isEmpty?.(extras[driverKey] ?? null) === true ? (
                    <UnavailableWidget reason="Nothing reported for this car yet." />
                  ) : hasChannels(entry) ? (
                    <entry.component
                      store={store}
                      driverKey={driverKey}
                      hiddenChannels={widget.hiddenChannels ?? NO_HIDDEN_CHANNELS}
                      onToggleChannel={(channelId) => toggleChannel(widget.instanceId, channelId)}
                    />
                  ) : (
                    <entry.component store={store} driverKey={driverKey} />
                  )}
                </div>
              </div>
            );
          })}
        </Responsive>
      </div>
    </section>
  );
}
