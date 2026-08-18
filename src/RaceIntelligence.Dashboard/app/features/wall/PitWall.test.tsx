import { act, fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import type { LiveConnection } from '../../shared/live/connection';
import { LiveStore } from '../../shared/live/store';
import { LiveContext } from '../../shared/live/useLive';
import { serialiseViewFile } from '../../shared/view/viewFile';
import {
  loadWallView,
  saveWallView,
  WALL_VIEW_VERSION,
  type WallWidget,
} from '../../shared/view/wallView';
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

/** The text of an exported wall, as a file somebody might choose. */
function viewFile(gameKey: string, widgets: WallWidget[]): File {
  return new File(
    [serialiseViewFile(gameKey, { version: WALL_VIEW_VERSION, widgets })],
    'wall.json',
    {
      type: 'application/json',
    },
  );
}

function savedWidget(widgetId: string): WallWidget {
  return { instanceId: `i-${widgetId}`, widgetId, driver: { slot: 1 }, x: 0, y: 0, w: 4, h: 6 };
}

/**
 * Chooses a file in the import picker.
 *
 * `files` is assigned through `defineProperty` because it is read-only on a real input and jsdom
 * honours that; there is no `DataTransfer` in jsdom to build one the legitimate way.
 */
async function importFile(file: File) {
  const input = screen.getByLabelText('Import a wall');
  Object.defineProperty(input, 'files', { value: [file], configurable: true });

  await act(async () => {
    fireEvent.change(input);
  });
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

  /**
   * A wall someone arranged over a weekend should survive the browser profile it was arranged in.
   */
  describe('export and import', () => {
    it('has nothing to export from an empty wall', () => {
      renderWall();

      expect(screen.getByRole('button', { name: 'Export' }).hasAttribute('disabled')).toBe(true);
    });

    it('hands the browser a file, and revokes the URL rather than pinning the blob', async () => {
      const created: string[] = [];
      const revoked: string[] = [];
      const realCreate = URL.createObjectURL;
      const realRevoke = URL.revokeObjectURL;

      URL.createObjectURL = () => {
        const url = `blob:test/${created.length}`;
        created.push(url);
        return url;
      };
      URL.revokeObjectURL = (url: string) => revoked.push(url);

      try {
        renderWall();
        await addWidget(DRIVER);

        await act(async () => {
          screen.getByRole('button', { name: 'Export' }).click();
        });

        expect(created).toHaveLength(1);
        expect(revoked).toEqual(created);
      } finally {
        URL.createObjectURL = realCreate;
        URL.revokeObjectURL = realRevoke;
      }
    });

    it('loads a wall from a file', async () => {
      renderWall();

      await importFile(viewFile(GAME, [savedWidget('reading')]));

      expect(screen.getByTestId('reading').textContent).toBe(DRIVER);
      expect(loadWallView(GAME).widgets).toHaveLength(1);
    });

    it('drops a widget this build does not have, and names it', async () => {
      renderWall();

      await importFile(viewFile(GAME, [savedWidget('reading'), savedWidget('from-the-future')]));

      expect(screen.getByRole('status').textContent).toContain('from-the-future');
      expect(loadWallView(GAME).widgets.map((w) => w.widgetId)).toEqual(['reading']);
    });

    /**
     * The distinction that is the substance of this feature: a widget this build does not have is
     * dropped, but a widget this *session* cannot feed is kept and left to explain itself. The
     * layout belongs to the user, and a session without brakes is not a reason to edit it.
     */
    it('keeps a widget this session cannot feed', async () => {
      renderWall(['Reading']);

      await importFile(viewFile(GAME, [savedWidget('gated')]));

      expect(screen.getByText(/No collector in this session reports/)).toBeTruthy();
      expect(loadWallView(GAME).widgets.map((w) => w.widgetId)).toEqual(['gated']);
    });

    it('offers a wall saved for another simulator rather than refusing it', async () => {
      renderWall();

      await importFile(viewFile('some-other-sim', [savedWidget('reading')]));

      expect(screen.getByRole('status').textContent).toContain('some-other-sim');
      // Offered means loaded. The capability check above already covers the parts that cannot run.
      expect(screen.getByTestId('reading')).toBeTruthy();
    });

    it('refuses a file that is not a wall, and leaves the wall alone', async () => {
      renderWall();
      await addWidget(DRIVER);

      await importFile(new File(['{ not json'], 'wall.json', { type: 'application/json' }));

      expect(screen.getByRole('status').textContent).toMatch(/not JSON/);
      // The contract of the control: choosing the wrong file must never cost you the arrangement
      // you already had, because there is no undo.
      expect(screen.getByTestId('reading').textContent).toBe(DRIVER);
      expect(loadWallView(GAME).widgets).toHaveLength(1);
    });

    it('round-trips the wall it exported', async () => {
      const first = renderWall(['Reading'], [DRIVER, OTHER_DRIVER]);
      await addWidget(OTHER_DRIVER);

      const exported = serialiseViewFile(GAME, loadWallView(GAME));
      first.unmount();
      window.localStorage.clear();

      renderWall(['Reading'], [DRIVER, OTHER_DRIVER]);
      await importFile(new File([exported], 'wall.json', { type: 'application/json' }));

      expect(screen.getByTestId('reading').textContent).toBe(OTHER_DRIVER);
      expect(loadWallView(GAME).widgets[0]?.driver).toEqual({ slot: 2 });
    });
  });
});
