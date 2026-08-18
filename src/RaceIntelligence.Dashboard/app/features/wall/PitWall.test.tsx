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
  driverKeys: readonly string[] = [DRIVER],
) {
  const store = new LiveStore();
  store.setFollowedDrivers(driverKeys);

  return render(
    <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
      <PitWall
        gameKey={GAME}
        capabilities={capabilities}
        driverKeys={driverKeys}
        displayName={(driverKey) => driverKey}
      />
    </LiveContext.Provider>,
  );
}

/** Opens the picker and places the offered widget against one car. */
async function addWidget(driverKey: string) {
  await act(async () => {
    screen.getByRole('button', { name: '+ Add widget' }).click();
  });
  await act(async () => {
    screen.getByRole('button', { name: driverKey }).click();
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
    expect(loadWallView(GAME).widgets[0]?.driverOrdinal).toBe(1);
  });

  /**
   * A saved wall meets sessions whose collectors differ. The tile stays and explains itself rather
   * than vanishing and leaving the user to wonder where their chart went.
   */
  it('keeps a widget this session cannot feed, and says why', () => {
    saveWallView(GAME, {
      version: WALL_VIEW_VERSION,
      widgets: [{ instanceId: 'a', widgetId: 'gated', driverOrdinal: 0, x: 0, y: 0, w: 4, h: 6 }],
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
        { instanceId: 'a', widgetId: 'retired-widget', driverOrdinal: 0, x: 0, y: 0, w: 4, h: 6 },
      ],
    });

    renderWall();

    expect(screen.getByText(/no widget called/)).toBeTruthy();
  });

  /**
   * The floor below which a widget stops being worth reading is the widget's judgement, and the
   * grid is what enforces it — so it has to reach the grid.
   */
  it('opens a widget at the size its catalogue entry asked for', async () => {
    const { container } = renderWall();

    await addWidget(DRIVER);

    expect(container.querySelector('.react-grid-item')).not.toBeNull();
    expect(loadWallView(GAME).widgets[0]?.w).toBe(4);
  });
});
