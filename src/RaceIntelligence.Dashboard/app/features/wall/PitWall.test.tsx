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
import {
  registerDefaultWall,
  registerSimPanels,
  type SimPanel,
  type SimPanelProps,
} from '../../sims/registry';
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

function renderWall(capabilities: readonly string[] = ['Reading'], driverKey: string = DRIVER) {
  const store = new LiveStore();
  store.setFollowedDrivers([driverKey]);

  return render(
    <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
      <PitWall
        gameKey={GAME}
        capabilities={capabilities}
        driverKey={driverKey}
        displayName={(key) => key}
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
  return { instanceId: `i-${widgetId}`, widgetId, x: 0, y: 0, w: 4, h: 6 };
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

/** Opens the picker and places a widget by name. One click, because a tile is about the open car. */
async function addWidget(title: string) {
  await act(async () => {
    screen.getByRole('button', { name: '+ Add widget' }).click();
  });
  await act(async () => {
    screen.getByRole('button', { name: title }).click();
  });
}

describe('PitWall', () => {
  beforeEach(() => {
    window.localStorage.clear();
    registerSimPanels(GAME, [readingPanel(), gatedPanel()]);
    registerDefaultWall(GAME, []);
  });

  it('starts empty and says so when the simulator suggests nothing', () => {
    renderWall();

    expect(screen.getByText(/Nothing on the wall/)).toBeTruthy();
  });

  it('adds a widget for the open car, and removes it again', async () => {
    renderWall();

    await addWidget('Reading');

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

    await addWidget('Reading');

    const saved = loadWallView(GAME);
    expect(saved?.widgets).toHaveLength(1);
    expect(saved?.widgets[0]?.widgetId).toBe('reading');

    first.unmount();
    renderWall();

    expect(screen.getByTestId('reading').textContent).toBe(DRIVER);
  });

  /**
   * The promise the whole document shape exists to keep. A wall names widgets and positions and
   * nothing else, so it can be opened against any session without a saved reference resolving to
   * the wrong car — the one mistake a race engineer cannot catch from the screen.
   */
  it('never persists a reference to a car, in any form', async () => {
    renderWall();

    await addWidget('Reading');

    const raw = window.localStorage.getItem(`pitwall:view:${GAME}`) ?? '';
    expect(raw).not.toContain(DRIVER);
    expect(raw).not.toMatch(/driver|slot|selected/);
    expect(Object.keys(loadWallView(GAME)?.widgets[0] ?? {})).toEqual([
      'instanceId',
      'widgetId',
      'x',
      'y',
      'w',
      'h',
    ]);
  });

  /**
   * Every tile is about whoever is open, so switching cars swings the whole wall at once — there is
   * no per-tile binding that could be left pointing at the previous driver.
   */
  it('shows whichever car is open', async () => {
    const first = renderWall(['Reading'], DRIVER);
    await addWidget('Reading');
    first.unmount();

    renderWall(['Reading'], OTHER_DRIVER);

    expect(screen.getByTestId('reading').textContent).toBe(OTHER_DRIVER);
  });

  /**
   * A wall nobody has arranged gets the simulator's suggestion, because an empty grid and a menu is
   * a puzzle rather than a dashboard.
   */
  it('seeds the simulator’s default arrangement on a wall nobody has saved', () => {
    registerDefaultWall(GAME, ['reading']);

    renderWall();

    expect(screen.getByTestId('reading')).toBeTruthy();
    expect(loadWallView(GAME)?.widgets).toHaveLength(1);
  });

  /**
   * And a wall somebody has emptied stays empty. Seeding over the top of that would be the
   * dashboard arguing with a choice the user made.
   */
  it('does not seed a wall the user has cleared', async () => {
    registerDefaultWall(GAME, ['reading']);

    const first = renderWall();
    await act(async () => {
      screen.getByRole('button', { name: 'Remove Reading' }).click();
    });
    first.unmount();

    renderWall();

    expect(screen.queryByTestId('reading')).toBeNull();
  });

  /** A suggestion this session cannot feed is not worth making. */
  it('leaves an unfeedable widget out of the default arrangement', () => {
    registerDefaultWall(GAME, ['reading', 'gated']);

    renderWall(['Reading']);

    expect(screen.getByTestId('reading')).toBeTruthy();
    expect(screen.queryByText(/No collector in this session reports/)).toBeNull();
  });

  /**
   * A saved wall meets sessions whose collectors differ. The tile stays and explains itself rather
   * than vanishing and leaving the user to wonder where their chart went.
   */
  it('keeps a widget this session cannot feed, and says why', () => {
    saveWallView(GAME, { version: WALL_VIEW_VERSION, widgets: [savedWidget('gated')] });

    renderWall(['Reading']);

    expect(screen.getByRole('heading', { name: /Gated channel/ })).toBeTruthy();
    expect(screen.getByText(/No collector in this session reports/)).toBeTruthy();
    expect(screen.queryByTestId('gated')).toBeNull();
  });

  it('says so when a saved widget is not in this build at all', () => {
    saveWallView(GAME, { version: WALL_VIEW_VERSION, widgets: [savedWidget('retired-widget')] });

    renderWall();

    expect(screen.getByText(/no widget called/)).toBeTruthy();
  });

  /**
   * A document from a build that stored a binding. The placement is somebody's arrangement and is
   * kept; the dead field is dropped rather than being written back out for ever.
   */
  it('keeps a tile saved with a binding, and forgets the binding', () => {
    window.localStorage.setItem(
      `pitwall:view:${GAME}`,
      JSON.stringify({
        version: WALL_VIEW_VERSION,
        widgets: [
          { instanceId: 'a', widgetId: 'reading', driver: { slot: 2 }, x: 0, y: 0, w: 4, h: 6 },
        ],
      }),
    );

    renderWall(['Reading'], DRIVER);

    expect(screen.getByTestId('reading').textContent).toBe(DRIVER);
    expect(Object.keys(loadWallView(GAME)?.widgets[0] ?? {})).not.toContain('driver');
  });

  it('opens a widget at the size its catalogue entry asked for', async () => {
    const { container } = renderWall();

    await addWidget('Reading');

    expect(container.querySelector('.react-grid-item')).not.toBeNull();
    expect(loadWallView(GAME)?.widgets[0]?.w).toBe(4);
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
        await addWidget('Reading');

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
      expect(loadWallView(GAME)?.widgets).toHaveLength(1);
    });

    it('drops a widget this build does not have, and names it', async () => {
      renderWall();

      await importFile(viewFile(GAME, [savedWidget('reading'), savedWidget('from-the-future')]));

      expect(screen.getByRole('status').textContent).toContain('from-the-future');
      expect(loadWallView(GAME)?.widgets.map((w) => w.widgetId)).toEqual(['reading']);
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
      expect(loadWallView(GAME)?.widgets.map((w) => w.widgetId)).toEqual(['gated']);
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
      await addWidget('Reading');

      await importFile(new File(['{ not json'], 'wall.json', { type: 'application/json' }));

      expect(screen.getByRole('status').textContent).toMatch(/not JSON/);
      // The contract of the control: choosing the wrong file must never cost you the arrangement
      // you already had, because there is no undo.
      expect(screen.getByTestId('reading').textContent).toBe(DRIVER);
      expect(loadWallView(GAME)?.widgets).toHaveLength(1);
    });

    it('round-trips the wall it exported', async () => {
      const first = renderWall();
      await addWidget('Reading');

      const exported = serialiseViewFile(GAME, loadWallView(GAME) ?? { version: 1, widgets: [] });
      first.unmount();
      window.localStorage.clear();

      renderWall();
      await importFile(new File([exported], 'wall.json', { type: 'application/json' }));

      expect(screen.getByTestId('reading').textContent).toBe(DRIVER);
      expect(loadWallView(GAME)?.widgets).toHaveLength(1);
    });
  });
});
