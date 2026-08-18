import { useCallback, useEffect, useMemo, useState } from 'react';
import { Responsive, useContainerWidth, type Layout } from 'react-grid-layout';
import 'react-grid-layout/css/styles.css';
import { useAllExtras, useLive } from '../../shared/live/useLive';
import {
  FIRST_SLOT,
  resolveBinding,
  type WallDriverBinding,
} from '../../shared/view/driverBinding';
import {
  loadWallView,
  saveWallView,
  WALL_VIEW_VERSION,
  type WallWidget,
} from '../../shared/view/wallView';
import { findPanel, isDriverWidget, panelsFor, WIDGET_GRID_COLUMNS } from '../../sims/registry';
import '../../sims/raceroom';

interface PitWallProps {
  gameKey: string;
  capabilities: readonly string[];
  /** The cars in the comparison, in slot order. A widget's `{ slot }` binding indexes this. */
  comparedDriverKeys: readonly string[];
  /** The car a `'selected'` widget is about, or null when no car is being watched. */
  selectedDriverKey: string | null;
  /** How a driver is named in a widget's header. */
  displayName: (driverKey: string) => string;
}

/**
 * Column counts by container width.
 *
 * Four steps, chosen for the monitor the wall is on rather than for a device class: a 1080p right
 * region, a 1440p one, a 4K one, and an ultrawide. `lg` is twelve because that is
 * {@link WIDGET_GRID_COLUMNS}, the vocabulary every catalogue entry's `defaultSize` is written in —
 * so a widget added on a 4K screen opens at exactly the size it declared, and the other breakpoints
 * are that layout rescaled.
 *
 * Fewer columns on a narrower wall rather than more, which reads backwards until you hold the
 * widget's width fixed: three four-column charts sit side by side at `lg` and the same three stack
 * two-up at `md`, each keeping enough pixels to still be a chart. More columns would shrink them
 * instead, which is how a stint trace becomes a smear.
 *
 * Measured against the grid's own container, not the viewport, because the timing column takes its
 * share first and the wall only ever gets what is left.
 */
const BREAKPOINTS = { xl: 2000, lg: 1400, md: 900, sm: 0 } as const;
const COLUMNS = { xl: 16, lg: WIDGET_GRID_COLUMNS, md: 8, sm: 4 } as const;

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
 * The pit wall: the widgets the user has placed, where they placed them.
 *
 * **Nothing here renders per focus frame.** The widgets are the same components the compare column
 * mounted, and they still paint themselves from `requestAnimationFrame` against the store's ring
 * buffers. This component re-renders when the layout changes — a drag, a resize, an add, a remove —
 * and at no other time, which is why dragging a tile does not compete with sixty frames a second of
 * telemetry.
 *
 * The layout is stored per simulator rather than per room; see `wallView.ts` for why that is the
 * right grain and what it costs.
 */
export function PitWall({
  gameKey,
  capabilities,
  comparedDriverKeys,
  selectedDriverKey,
  displayName,
}: PitWallProps) {
  const { store } = useLive();
  const { width, containerRef } = useContainerWidth();

  // A widget can report that it has nothing to say for a car — incident points on a sim that
  // reports none, for instance. Honoured here for the same reason #46 made the strip honour it: a
  // frame around four dashes is worse than no frame, and worse still on a wall, where the user put
  // the tile there on purpose and deserves to know it is waiting rather than broken.
  const extras = useAllExtras();

  const [widgets, setWidgets] = useState<WallWidget[]>(() => loadWallView(gameKey).widgets);
  const [picking, setPicking] = useState(false);

  // Reloaded during render rather than from an effect, which is React's advice for state derived
  // from a prop: an effect would paint one frame of the previous simulator's wall against this
  // session before correcting itself. The same shape `SessionView` uses for its expanded rows.
  const [loadedGameKey, setLoadedGameKey] = useState(gameKey);
  if (loadedGameKey !== gameKey) {
    setLoadedGameKey(gameKey);
    setWidgets(loadWallView(gameKey).widgets);
    setPicking(false);
  }

  useEffect(() => {
    saveWallView(gameKey, { version: WALL_VIEW_VERSION, widgets });
  }, [gameKey, widgets]);

  // Sorted and deduplicated for the reason `FocusPanel` does it: with two collectors in a room the
  // same capability arrives twice, and an unstable list would rebuild the picker on every
  // room-list message.
  const stableCapabilities = useMemo(() => [...new Set(capabilities)].sort(), [capabilities]);

  /** What the picker offers: everything this room can actually feed. */
  const addable = useMemo(
    () => panelsFor(gameKey, stableCapabilities).filter(isDriverWidget),
    [gameKey, stableCapabilities],
  );

  const available = useMemo(() => new Set(addable.map((panel) => panel.id)), [addable]);

  const addWidget = useCallback(
    (widgetId: string, driver: WallDriverBinding) => {
      const entry = findPanel(gameKey, widgetId);
      if (entry === null) {
        return;
      }

      setWidgets((current) => [
        ...current,
        {
          instanceId: newInstanceId(),
          widgetId,
          driver,
          // Dropped at the bottom of the wall, where there is always room. Placing it in the first
          // gap would be cleverer and worse: a widget appearing somewhere in the middle of an
          // arrangement the user built is a widget they then have to go and find.
          x: 0,
          y: Number.MAX_SAFE_INTEGER,
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
   * Writes a drag or a resize back.
   *
   * Merged into the existing widgets rather than rebuilt from the layout, because the layout only
   * carries geometry — the widget id and the driver it is bound to live here and would be lost by
   * a rebuild.
   */
  const onLayoutChange = useCallback((layout: Layout) => {
    const geometry = new Map(layout.map((item) => [item.i, item]));

    setWidgets((current) =>
      current.map((widget) => {
        const item = geometry.get(widget.instanceId);
        if (item === undefined) {
          return widget;
        }

        return { ...widget, x: item.x, y: item.y, w: item.w, h: item.h };
      }),
    );
  }, []);

  /**
   * The grid's own view of the wall, with each widget's minimum size attached.
   *
   * `minW`/`minH` come off the catalogue entry, so the floor below which a widget stops being worth
   * reading is set by the widget and enforced by the grid — the drag simply stops. A widget whose
   * entry has gone missing gets no floor, because there is nobody left to ask.
   */
  const layout = useMemo<Layout>(
    () =>
      widgets.map((widget) => {
        const entry = findPanel(gameKey, widget.widgetId);

        return {
          i: widget.instanceId,
          x: widget.x,
          y: widget.y,
          w: widget.w,
          h: widget.h,
          ...(entry === null ? {} : { minW: entry.minSize.w, minH: entry.minSize.h }),
        };
      }),
    [gameKey, widgets],
  );

  return (
    <section className="wall" aria-label="Pit wall">
      <header className="wall__header">
        <h2 className="wall__title">Pit wall</h2>
        <button
          type="button"
          className="link-button"
          aria-expanded={picking}
          onClick={() => setPicking((open) => !open)}
        >
          {picking ? 'Cancel' : '+ Add widget'}
        </button>
      </header>

      {picking && (
        <div className="wall__picker">
          {addable.length === 0 && (
            <p className="wall__note">
              This session reports no channels that can be put on the wall.
            </p>
          )}

          {/*
            Two ways to place a tile, and the difference is worth the extra button. "Selected"
            follows whichever car you click in the tower, so one set of tiles serves a whole field;
            a named car pins the tile to that slot, which is what a side-by-side comparison is made
            of. The pinned buttons are labelled with the driver rather than "slot 2", because the
            car is what the user is thinking about — the slot is only how it is written down.
          */}
          {addable.map((panel) => (
            <div key={panel.id} className="wall__picker-row">
              <span className="wall__picker-name">{panel.title}</span>

              {comparedDriverKeys.length > 0 && (
                <button
                  type="button"
                  className="link-button"
                  onClick={() => addWidget(panel.id, 'selected')}
                >
                  Selected car
                </button>
              )}

              {comparedDriverKeys.map((driverKey, index) => (
                <button
                  key={driverKey}
                  type="button"
                  className="link-button"
                  onClick={() => addWidget(panel.id, { slot: index + FIRST_SLOT })}
                >
                  {displayName(driverKey)}
                </button>
              ))}
            </div>
          ))}

          {addable.length > 0 && comparedDriverKeys.length === 0 && (
            <p className="wall__note">
              Open a driver&apos;s telemetry from the tower first — every widget here is about one
              car.
            </p>
          )}
        </div>
      )}

      <div ref={containerRef} className="wall__grid">
        {widgets.length === 0 && (
          <p className="wall__note">
            Nothing on the wall yet. Add the charts you want in front of you and they will be here
            next time.
          </p>
        )}

        <Responsive
          width={width}
          layouts={{ xl: layout, lg: layout, md: layout, sm: layout }}
          breakpoints={BREAKPOINTS}
          cols={COLUMNS}
          rowHeight={ROW_HEIGHT}
          onLayoutChange={onLayoutChange}
          dragConfig={DRAG_CONFIG}
        >
          {widgets.map((widget) => {
            const entry = findPanel(gameKey, widget.widgetId);
            const driverKey = resolveBinding(widget.driver, comparedDriverKeys, selectedDriverKey);

            return (
              <div key={widget.instanceId} className="wall__widget">
                <header className="wall__widget-head">
                  <span className="wall__widget-grip" aria-hidden="true">
                    ⠿
                  </span>
                  <h3 className="wall__widget-title">
                    {entry?.title ?? widget.widgetId}
                    {driverKey !== undefined && (
                      <span className="wall__widget-driver"> · {displayName(driverKey)}</span>
                    )}
                  </h3>
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
                  ) : driverKey === undefined ? (
                    <UnavailableWidget
                      reason={
                        widget.driver === undefined
                          ? // A driver widget with no binding at all, which a document written by
                            // an earlier build produces. Saying so beats the slot message, which
                            // would send someone looking for a car to put in a slot that is not
                            // there; removing it and be done would silently edit their wall.
                            'This tile was saved without a car. Remove it and add it again.'
                          : widget.driver === 'selected'
                            ? 'No car is selected. Open one from the tower.'
                            : 'No car in this slot yet.'
                      }
                    />
                  ) : entry.isEmpty?.(extras[driverKey] ?? null) === true ? (
                    <UnavailableWidget reason="Nothing reported for this car yet." />
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
