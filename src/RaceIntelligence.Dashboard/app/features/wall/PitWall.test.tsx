import { act, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import type { LiveConnection } from '../../shared/live/connection';
import { LiveStore } from '../../shared/live/store';
import { LiveContext } from '../../shared/live/useLive';
import { loadWallView, saveWallView, WALL_VIEW_VERSION } from '../../shared/view/wallView';
import { registerSimPanels, type SimPanel, type SimPanelProps } from '../../sims/registry';
import { PitWall } from './PitWall';

const GAME = 'wall-test';
const DRIVER = 'id:2';
const OTHER_DRIVER = 'id:9';

function readingPanel(): SimPanel {
  return {
    id: 'reading',
    title: 'Reading',
    scope: 'driver',
    requires: ['Reading'],
    component: ({ driverKey }: SimPanelProps) => <span data-testid="reading">{driverKey}</span>,
    defaultSize: { w: 4, h: 6 },
    minSize: { w: 3, h: 4 },
  };
}

function gatedPanel(): SimPanel {
  return {
    id: 'gated',
    title: 'Gated channel',
    scope: 'driver',
    requires: ['NeverReported'],
    component: () => <span data-testid="gated">gated</span>,
    defaultSize: { w: 4, h: 6 },
    minSize: { w: 2, h: 2 },
  };
}

function renderWall(
  capabilities: readonly string[] = ['Reading'],
  comparedDriverKeys: readonly string[] = [DRIVER],
  selectedDriverKey: string | null = comparedDriverKeys[0] ?? null,
) {
  const store = new LiveStore();
  store.setFollowedDrivers(comparedDriverKeys);

  return render(
    <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
      <PitWall
        gameKey={GAME}
        capabilities={capabilities}
        comparedDriverKeys={comparedDriverKeys}
        selectedDriverKey={selectedDriverKey}
        displayName={(driverKey) => driverKey}
      />
    </LiveContext.Provider>,
  );
}

/** Opens the picker and places the offered widget, pinned to a car or bound to the selection. */
async function addWidget(binding: string) {
  await act(async () => {
    screen.getByRole('button', { name: '+ Add widget' }).click();
  });
  await act(async () => {
    screen.getByRole('button', { name: binding }).click();
  });
}

describe('PitWall', () => {
  beforeEach(() => {
    window.localStorage.clear();
    registerSimPanels(GAME, [readingPanel(), gatedPanel()]);
  });

  it('starts empty and says so', () => {
    renderWall();

    expect(screen.getByText(/Nothing on the wall yet/)).toBeTruthy();
  });

  it('adds a widget for the chosen car, and removes it again', async () => {
    renderWall();

    await addWidget(DRIVER);

    expect(screen.getByTestId('reading').textContent).toBe(DRIVER);

    await act(async () => {
      screen.getByRole('button', { name: 'Remove Reading' }).click();
    });

    expect(screen.queryByTestId('reading')).toBeNull();
  });

  /**
   * The wall is saved per simulator, so two rooms of the same sim open the same arrangement.
   */
  it('persists the wall and reads it back', async () => {
    const first = renderWall();

    await addWidget(DRIVER);

    const saved = loadWallView(GAME);
    expect(saved.widgets).toHaveLength(1);
    expect(saved.widgets[0]?.widgetId).toBe('reading');

    first.unmount();
    renderWall();

    expect(screen.getByTestId('reading').textContent).toBe(DRIVER);
  });

  /**
   * No driver key is ever written to storage — see `wallView.ts`. A key names one car in one
   * session and nobody in the next, and the same wall is opened against every session of that sim.
   */
  it('never persists a driver key', async () => {
    renderWall(['Reading'], [DRIVER, OTHER_DRIVER]);

    await addWidget(OTHER_DRIVER);

    const raw = window.localStorage.getItem(`pitwall:view:${GAME}`) ?? '';
    expect(raw).not.toContain(OTHER_DRIVER);
    expect(loadWallView(GAME).widgets[0]?.driver).toEqual({ slot: 2 });
  });

  /**
   * A saved wall meets sessions whose collectors differ. The tile stays and explains itself rather
   * than vanishing and leaving the user to wonder where their chart went.
   */
  it('keeps a widget this session cannot feed, and says why', () => {
    saveWallView(GAME, {
      version: WALL_VIEW_VERSION,
      widgets: [
        { instanceId: 'a', widgetId: 'gated', driver: { slot: 1 }, x: 0, y: 0, w: 4, h: 6 },
      ],
    });

    renderWall(['Reading']);

    expect(screen.getByRole('heading', { name: /Gated channel/ })).toBeTruthy();
    expect(screen.getByText(/No collector in this session reports/)).toBeTruthy();
    expect(screen.queryByTestId('gated')).toBeNull();
  });

  it('says so when a saved widget is not in this build at all', () => {
    saveWallView(GAME, {
      version: WALL_VIEW_VERSION,
      widgets: [
        {
          instanceId: 'a',
          widgetId: 'retired-widget',
          driver: { slot: 1 },
          x: 0,
          y: 0,
          w: 4,
          h: 6,
        },
      ],
    });

    renderWall();

    expect(screen.getByText(/no widget called/)).toBeTruthy();
  });

  /**
   * The floor below which a widget stops being worth reading is the widget's judgement, and the
   * grid is what enforces it — so it has to reach the grid.
   */
  /**
   * The binding that makes a compact wall work across a whole field: one set of tiles that swings
   * to whichever car is being looked at, rather than one set per car.
   */
  it('follows the selection when bound to it', async () => {
    const first = renderWall(['Reading'], [DRIVER, OTHER_DRIVER], DRIVER);

    await addWidget('Selected car');

    expect(screen.getByTestId('reading').textContent).toBe(DRIVER);
    expect(loadWallView(GAME).widgets[0]?.driver).toBe('selected');

    // The same wall, the same tile, a different car selected.
    first.unmount();
    renderWall(['Reading'], [DRIVER, OTHER_DRIVER], OTHER_DRIVER);

    expect(screen.getByTestId('reading').textContent).toBe(OTHER_DRIVER);
  });

  /**
   * A pinned tile stays on its car while the selection moves, which is what makes two of them a
   * comparison rather than two views of the same thing.
   */
  it('stays on its slot while the selection moves', async () => {
    const first = renderWall(['Reading'], [DRIVER, OTHER_DRIVER], DRIVER);

    await addWidget(OTHER_DRIVER);
    first.unmount();

    renderWall(['Reading'], [DRIVER, OTHER_DRIVER], DRIVER);

    expect(screen.getByTestId('reading').textContent).toBe(OTHER_DRIVER);
  });

  /**
   * A wall saved with two cars, opened against a session where one is being watched. Saying the
   * slot is empty is the only honest answer; sliding the tile onto the remaining car would put one
   * driver's numbers under another driver's heading.
   */
  it('says a slot is empty rather than showing the wrong car', () => {
    saveWallView(GAME, {
      version: WALL_VIEW_VERSION,
      widgets: [
        { instanceId: 'a', widgetId: 'reading', driver: { slot: 2 }, x: 0, y: 0, w: 4, h: 6 },
      ],
    });

    renderWall(['Reading'], [DRIVER], DRIVER);

    expect(screen.getByText(/No car in this slot yet/)).toBeTruthy();
    expect(screen.queryByTestId('reading')).toBeNull();
  });

  /**
   * A document from the build that stored an ordinal instead of a binding.
   *
   * The tile keeps its place and says it has no car, rather than being dropped or bound to
   * whichever car happens to sit at that index. Both alternatives are worse in the same way: they
   * change an arrangement someone made, without telling them.
   */
  it('says a tile has no car when its binding predates this build', () => {
    window.localStorage.setItem(
      `pitwall:view:${GAME}`,
      JSON.stringify({
        version: WALL_VIEW_VERSION,
        widgets: [
          { instanceId: 'a', widgetId: 'reading', driverOrdinal: 0, x: 0, y: 0, w: 4, h: 6 },
        ],
      }),
    );

    renderWall(['Reading'], [DRIVER], DRIVER);

    expect(screen.getByText(/saved without a car/)).toBeTruthy();
    expect(screen.queryByTestId('reading')).toBeNull();
  });

  it('opens a widget at the size its catalogue entry asked for', async () => {
    const { container } = renderWall();

    await addWidget(DRIVER);

    expect(container.querySelector('.react-grid-item')).not.toBeNull();
    expect(loadWallView(GAME).widgets[0]?.w).toBe(4);
  });
});
