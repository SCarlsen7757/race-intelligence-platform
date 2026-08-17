import type { ComponentType } from 'react';
import type { LiveStore } from '../shared/live/store';

/**
 * Props every focus panel receives.
 *
 * The driver key is explicit rather than implied by the store because two drivers can be compared
 * side by side: the same panel type is mounted twice, against one store, and nothing about it may
 * assume there is only one car being watched.
 */
export interface SimPanelProps {
  store: LiveStore;
  driverKey: string;
}

/**
 * A panel the focus view can show, and what it needs in order to mean anything.
 */
export interface SimPanel {
  id: string;
  title: string;
  /**
   * `SimCapabilities` flag names this panel requires — **all** of them, not any.
   *
   * The platform's rule for staying simulator-agnostic, restated on the frontend: a panel declares
   * what data it needs, never which simulator it wants. Adding a second simulator then means its
   * connector reporting an accurate capability set, with no change here.
   */
  requires: readonly string[];
  component: ComponentType<SimPanelProps>;
}

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
