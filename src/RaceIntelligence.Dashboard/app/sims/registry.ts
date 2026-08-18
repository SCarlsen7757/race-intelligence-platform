import type { ComponentType } from 'react';
import type { ExtrasFrameMessage } from '../shared/live/contracts';
import type { LiveStore } from '../shared/live/store';

/**
 * Props a widget describing the session itself receives.
 *
 * No driver key, and that absence is the point: a timing tower or a pit-window banner is about the
 * room, and handing it a car would invite it to quietly become about that car instead.
 */
export interface RoomPanelProps {
  store: LiveStore;
}

/**
 * Props every widget describing one car receives.
 *
 * The driver key is explicit rather than implied by the store because several drivers can be
 * compared side by side: the same widget type is mounted once per car, against one store, and
 * nothing about it may assume there is only one being watched.
 */
export interface SimPanelProps extends RoomPanelProps {
  driverKey: string;
}

/**
 * How much of the wall a widget asks for, in grid cells.
 *
 * Cells rather than pixels because the pit wall is a grid the user rearranges, and a widget that
 * asked in pixels would be asking about a monitor it cannot see. The column count lives on
 * {@link WIDGET_GRID_COLUMNS}; the row height is the grid's to decide.
 */
export interface WidgetSize {
  w: number;
  h: number;
}

/**
 * How many columns the pit wall's grid is divided into.
 *
 * Twelve because it divides by two, three and four, so halves, thirds and quarters of the wall are
 * all expressible without a widget having to ask for 4.5 of anything.
 */
export const WIDGET_GRID_COLUMNS = 12;

/** What every catalogue entry declares, whatever it is about. */
interface WidgetBase {
  id: string;
  title: string;
  /** Optional visual stack shared with related panels (for example tyre channels). */
  group?: {
    id: string;
    title: string;
    itemTitle: string;
  };
  /**
   * `SimCapabilities` flag names this panel requires — **all** of them, not any.
   *
   * The platform's rule for staying simulator-agnostic, restated on the frontend: a panel declares
   * what data it needs, never which simulator it wants. Adding a second simulator then means its
   * connector reporting an accurate capability set, with no change here.
   */
  requires: readonly string[];
  /**
   * The size the widget opens at when the user adds it to the wall.
   *
   * Declared by the widget rather than by the grid, because the widget is what knows what it is
   * for. Four wheels of tyre temperature over a fifteen-minute stint needs width to be a stint and
   * not a smear; a damage meter is four short bars and gains nothing from more.
   */
  defaultSize: WidgetSize;
  /**
   * The size below which the widget stops being worth reading.
   *
   * This is what the grid clamps a drag to, and it is a judgement only the widget can make. It is
   * deliberately a floor on *usefulness*, not on legibility of the frame: a chart squeezed to two
   * columns still draws, which is exactly why something has to stop the user from getting there and
   * concluding the chart is broken.
   */
  minSize: WidgetSize;
  /** Whether this panel has nothing to say for one driver, given that driver's latest extras. */
  isEmpty?: (extras: ExtrasFrameMessage | null) => boolean;
}

/** A widget about one car. Mounted once per driver on screen. */
export interface DriverWidget extends WidgetBase {
  scope: 'driver';
  component: ComponentType<SimPanelProps>;
}

/** A widget about the session. Mounted once, however many cars are being watched. */
export interface RoomWidget extends WidgetBase {
  scope: 'room';
  component: ComponentType<RoomPanelProps>;
}

/**
 * One entry in the widget catalogue.
 *
 * A union discriminated on `scope` rather than one shape with an optional driver key, so that "a
 * room widget is never bound to a car" holds by construction: there is no driver key in
 * {@link RoomPanelProps} for a caller to pass, and no way to mount a {@link DriverWidget} without
 * one. The alternative — an optional key that room widgets promise not to read — is a promise the
 * compiler cannot keep.
 */
export type SimPanel = DriverWidget | RoomWidget;

const registry = new Map<string, SimPanel[]>();

/**
 * Registers panels specific to one simulator.
 *
 * Keyed by game key because some panels genuinely are simulator-specific in presentation — a
 * RaceRoom push-to-pass readout and another simulator's ERS deployment are not the same widget
 * even where they share a capability flag. The capability check above governs whether a panel can
 * show at all; the game key governs which build of it.
 */
export function registerSimPanels(gameKey: string, panels: SimPanel[]): void {
  registry.set(gameKey, panels);
}

/** The panels a room can show, given what its collectors report they can produce. */
export function panelsFor(gameKey: string, capabilities: readonly string[]): SimPanel[] {
  const available = new Set(capabilities);

  return (registry.get(gameKey) ?? []).filter((panel) =>
    panel.requires.every((capability) => available.has(capability)),
  );
}

/**
 * Narrows a catalogue entry to the ones a driver column can mount.
 *
 * A type predicate rather than a bare `scope === 'driver'` test at each call site, because the
 * whole value of the union is that the compiler refuses to mount a room widget with a driver key —
 * and a `filter` without a predicate hands back the union again and throws that away.
 */
export function isDriverWidget(panel: SimPanel): panel is DriverWidget {
  return panel.scope === 'driver';
}
