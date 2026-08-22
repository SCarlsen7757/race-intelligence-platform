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
  type SimPanelDeclaration,
  type ChannelPanelProps,
  type SimPanelProps,
} from '../../sims/registry';
import { PitWall } from './PitWall';

const GAME = 'wall-test';
const DRIVER = 'id:2';
const OTHER_DRIVER = 'id:9';

function readingPanel(): SimPanelDeclaration {
  return {
    id: 'reading',
    title: 'Reading',
    scope: 'driver',
    requires: ['Reading'],
    component: ({ driverKey }: SimPanelProps) => <span data-testid="reading">{driverKey}</span>,
    defaultSize: { w: 4, h: 6 },
  };
}

function gatedPanel(): SimPanelDeclaration {
  return {
    id: 'gated',
    title: 'Gated channel',
    scope: 'driver',
    requires: ['NeverReported'],
    component: () => <span data-testid="gated">gated</span>,
    defaultSize: { w: 4, h: 6 },
  };
}

/**
 * A chart widget, which is the only kind that has channels to turn off.
 *
 * Renders its hidden set as text so a test can read what the wall handed it, and a button per
 * channel so a test can toggle one the way the legend does.
 */
function chartPanel(): SimPanelDeclaration {
  return {
    id: 'chart',
    title: 'Chart',
    scope: 'driver',
    requires: ['Reading'],
    channels: [
      { id: 'fl', label: 'FL' },
      { id: 'fr', label: 'FR' },
    ],
    component: ({ hiddenChannels, onToggleChannel }: ChannelPanelProps) => (
      <span data-testid="chart">
        <span data-testid="chart-hidden">{[...hiddenChannels].sort().join(',')}</span>
        <button type="button" onClick={() => onToggleChannel('fl')}>
          toggle fl
        </button>
      </span>
    ),
    defaultSize: { w: 4, h: 6 },
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

/**
 * Lets the wall's trailing save run.
 *
 * The wall does not write to storage on every change — the grid fires a layout callback per frame
 * of a drag, and `localStorage` is synchronous on the thread every chart paints from. So anything
 * asserting on what was saved has to wait for the delay rather than reading straight after the
 * click. See `SAVE_DELAY_MS`.
 */
async function settle() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 500));
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
    registerSimPanels(GAME, [readingPanel(), gatedPanel(), chartPanel()]);
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

    await settle();
    const saved = loadWallView(GAME);
    expect(saved?.widgets).toHaveLength(1);
    expect(saved?.widgets[0]?.widgetId).toBe('reading');

    first.unmount();
    renderWall();

    expect(screen.getByTestId('reading').textContent).toBe(DRIVER);
  });

  /**
   * The grid fires its layout callback continuously through a drag rather than once when it ends,
   * and `localStorage` is synchronous on the same thread every chart paints from — so a save wired
   * straight to that callback wrote the whole view dozens of times a second while a tile was being
   * moved, and the cost landed as the traces stuttering.
   */
  describe('writing to storage', () => {
    it('does not write until the changes have stopped', async () => {
      renderWall();

      await addWidget('Reading');

      expect(window.localStorage.getItem(`pitwall:view:${GAME}`)).toBeNull();

      await settle();

      expect(loadWallView(GAME)?.widgets).toHaveLength(1);
    });

    /** Leaving the session inside the delay must never cost somebody their arrangement. */
    it('writes what is pending when the wall goes away', async () => {
      const first = renderWall();

      await addWidget('Reading');
      expect(window.localStorage.getItem(`pitwall:view:${GAME}`)).toBeNull();

      first.unmount();

      expect(loadWallView(GAME)?.widgets).toHaveLength(1);
    });
  });

  /**
   * The wall's only way back.
   *
   * It is otherwise a one-way door — `loadWallView` tells "never arranged" from "arranged empty" on
   * purpose, so clearing every tile does not bring the default back, and there is no undo. That was
   * survivable while every arrangement was reachable by pointer, and stopped being so as soon as a
   * geometry the user did not choose could put a tile where its Remove button cannot be clicked.
   */
  describe('starting over', () => {
    beforeEach(() => {
      registerDefaultWall(GAME, ['reading']);
    });

    it('puts the simulator’s starting wall back', async () => {
      renderWall();

      await act(async () => {
        screen.getByRole('button', { name: 'Remove Reading' }).click();
      });
      expect(screen.queryByTestId('reading')).toBeNull();

      await act(async () => {
        screen.getByRole('button', { name: 'Reset' }).click();
      });

      expect(screen.getByTestId('reading')).toBeTruthy();
    });

    it('replaces a wall that has been arranged somewhere unreachable', async () => {
      saveWallView(GAME, {
        version: WALL_VIEW_VERSION,
        widgets: [{ instanceId: 'i-lost', widgetId: 'reading', x: 9999, y: 0, w: 9999, h: 6 }],
      });

      renderWall();

      await act(async () => {
        screen.getByRole('button', { name: 'Reset' }).click();
      });

      await settle();
      expect(loadWallView(GAME)?.widgets).toHaveLength(1);
      await settle();
      expect(loadWallView(GAME)?.widgets[0]?.instanceId).toBe('default-reading');
    });

    it('says what it did, because a wall vanishing would otherwise read as a fault', async () => {
      renderWall();

      await act(async () => {
        screen.getByRole('button', { name: 'Reset' }).click();
      });

      expect(screen.getByText(/starting wall/i)).toBeTruthy();
    });
  });

  /**
   * A placement outside the grid is a tile with no Remove button the user can reach. The file is
   * meant to be hand-edited and shared, so this arrives legitimately rather than as an attack.
   */
  it('pulls a widget saved outside the grid back into it', async () => {
    saveWallView(GAME, {
      version: WALL_VIEW_VERSION,
      widgets: [{ instanceId: 'i-lost', widgetId: 'reading', x: 400, y: 0, w: 9999, h: 6 }],
    });

    renderWall();

    await settle();
    const saved = loadWallView(GAME)?.widgets[0];
    expect(saved?.w).toBeLessThanOrEqual(12);
    expect(saved?.x).toBeLessThanOrEqual(12);
    expect(screen.getByTestId('reading')).toBeTruthy();
  });

  /**
   * The promise the whole document shape exists to keep. A wall names widgets and positions and
   * nothing else, so it can be opened against any session without a saved reference resolving to
   * the wrong car — the one mistake a race engineer cannot catch from the screen.
   */
  it('never persists a reference to a car, in any form', async () => {
    renderWall();

    await addWidget('Reading');

    await settle();
    const raw = window.localStorage.getItem(`pitwall:view:${GAME}`) ?? '';
    expect(raw).not.toContain(DRIVER);
    expect(raw).not.toMatch(/driver|slot|selected/);

    // A closed vocabulary rather than an exact list: `at` and `hiddenChannels` are present only
    // when the user has arranged a second width or turned a channel off, so demanding an exact set
    // would make this test about which optional fields happened to be written. What it is actually
    // for is that nothing *else* can ever ride along — which is `normaliseWidget`'s job, and is
    // what stopped the old driver bindings being written back out forever.
    const allowed = ['instanceId', 'widgetId', 'x', 'y', 'w', 'h', 'at', 'hiddenChannels'];
    await settle();
    const saved = Object.keys(loadWallView(GAME)?.widgets[0] ?? {});

    expect(saved).toEqual(expect.arrayContaining(['instanceId', 'widgetId', 'x', 'y', 'w', 'h']));
    expect(saved.filter((key) => !allowed.includes(key))).toEqual([]);
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
  it('seeds the simulator’s default arrangement on a wall nobody has saved', async () => {
    registerDefaultWall(GAME, ['reading']);

    renderWall();

    expect(screen.getByTestId('reading')).toBeTruthy();
    await settle();
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
  it('keeps a tile saved with a binding, and forgets the binding', async () => {
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
    await settle();
    expect(Object.keys(loadWallView(GAME)?.widgets[0] ?? {})).not.toContain('driver');
  });

  it('opens a widget at the size its catalogue entry asked for', async () => {
    const { container } = renderWall();

    await addWidget('Reading');

    expect(container.querySelector('.react-grid-item')).not.toBeNull();
    await settle();
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
      await settle();
      expect(loadWallView(GAME)?.widgets).toHaveLength(1);
    });

    it('drops a widget this build does not have, and names it', async () => {
      renderWall();

      await importFile(viewFile(GAME, [savedWidget('reading'), savedWidget('from-the-future')]));

      expect(screen.getByRole('status').textContent).toContain('from-the-future');
      await settle();
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
      await settle();
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
      await settle();
      expect(loadWallView(GAME)?.widgets).toHaveLength(1);
    });

    it('round-trips the wall it exported', async () => {
      const first = renderWall();
      await addWidget('Reading');

      await settle();
      const exported = serialiseViewFile(GAME, loadWallView(GAME) ?? { version: 1, widgets: [] });
      first.unmount();
      window.localStorage.clear();

      renderWall();
      await importFile(new File([exported], 'wall.json', { type: 'application/json' }));

      expect(screen.getByTestId('reading').textContent).toBe(DRIVER);
      await settle();
      expect(loadWallView(GAME)?.widgets).toHaveLength(1);
    });
  });
  /**
   * The arrangement this whole feature exists for: two tyre-wear tiles, one narrowed to the front
   * left and one to the rear right. It only works if visibility belongs to the placement, so this
   * is the test that would fail if it ever became a property of the widget type.
   */
  describe('channel toggles', () => {
    it('starts with every channel shown', async () => {
      renderWall();
      await addWidget('Chart');

      expect(screen.getByTestId('chart-hidden').textContent).toBe('');
    });

    it('hides a channel for one tile without touching another of the same widget', async () => {
      renderWall();
      await addWidget('Chart');
      await addWidget('Chart');

      const tiles = screen.getAllByTestId('chart');
      expect(tiles).toHaveLength(2);

      await act(async () => {
        (tiles[0]!.querySelector('button') as HTMLButtonElement).click();
      });

      const hidden = screen.getAllByTestId('chart-hidden');
      expect(hidden[0]!.textContent).toBe('fl');
      expect(hidden[1]!.textContent).toBe('');
    });

    it('turns a channel back on', async () => {
      renderWall();
      await addWidget('Chart');

      const click = async () => {
        await act(async () => {
          (screen.getByTestId('chart').querySelector('button') as HTMLButtonElement).click();
        });
      };

      await click();
      expect(screen.getByTestId('chart-hidden').textContent).toBe('fl');

      await click();
      expect(screen.getByTestId('chart-hidden').textContent).toBe('');
    });

    /** A wall of one-corner-per-tile is worth arranging only if it is still there tomorrow. */
    it('remembers which channels were hidden', async () => {
      const first = renderWall();
      await addWidget('Chart');

      await act(async () => {
        (screen.getByTestId('chart').querySelector('button') as HTMLButtonElement).click();
      });

      await settle();
      expect(loadWallView(GAME)?.widgets[0]?.hiddenChannels).toEqual(['fl']);

      first.unmount();
      renderWall();

      expect(screen.getByTestId('chart-hidden').textContent).toBe('fl');
    });

    /**
     * A wall saved before channels had toggles carries nothing here, and must come back drawing
     * everything — which is what its author saw when they saved it. The same holds when a widget
     * gains a channel in a later build, which is why the field records what to *hide*.
     */
    it('shows every channel for a wall saved without any record of them', () => {
      saveWallView(GAME, { version: WALL_VIEW_VERSION, widgets: [savedWidget('chart')] });
      renderWall();

      expect(screen.getByTestId('chart-hidden').textContent).toBe('');
    });

    it('carries hidden channels through an export and back', async () => {
      const first = renderWall();
      await addWidget('Chart');

      await act(async () => {
        (screen.getByTestId('chart').querySelector('button') as HTMLButtonElement).click();
      });

      await settle();
      const exported = serialiseViewFile(GAME, loadWallView(GAME) ?? { version: 1, widgets: [] });
      first.unmount();
      window.localStorage.clear();

      renderWall();
      await importFile(new File([exported], 'wall.json', { type: 'application/json' }));

      expect(screen.getByTestId('chart-hidden').textContent).toBe('fl');
    });
  });

  /**
   * Move and resize are the two gestures a mouse alone could reach before this — see #83.
   *
   * The move tests place the tile narrower than the wall (`w: 2` against `sm`'s 4 columns) and step
   * it sideways rather than down. A vertical move is the wrong thing to assert on here: the vertical
   * compactor packs a lone tile back to `y: 0` on every step — precisely the same thing a pointer
   * drag's own `onDrag` handler does, since it compacts after every `moveElement` call too — so a
   * one-cell nudge down and a compact leaves a solitary tile exactly where it started regardless of
   * input method. `x` has no such floor to snap back to, so it is what actually proves a keypress
   * reached the grid. The resize test grows `h`, which has no compaction to contend with either way.
   */
  describe('keyboard arrange', () => {
    function narrowWidget(): WallWidget {
      return { instanceId: 'i-reading', widgetId: 'reading', x: 0, y: 0, w: 2, h: 6 };
    }

    it('moves a tile with the grip, live and revertible', async () => {
      saveWallView(GAME, { version: WALL_VIEW_VERSION, widgets: [narrowWidget()] });
      renderWall();

      const tile = screen.getByRole('group');
      const atRest = tile.getAttribute('style');
      const grip = screen.getByRole('button', { name: 'Move Reading' });

      await act(async () => {
        fireEvent.keyDown(grip, { key: 'Enter' });
      });
      expect(grip.getAttribute('aria-pressed')).toBe('true');

      await act(async () => {
        fireEvent.keyDown(grip, { key: 'ArrowRight' });
      });
      const whileMoving = tile.getAttribute('style');
      expect(whileMoving).not.toBe(atRest);

      await act(async () => {
        fireEvent.keyDown(grip, { key: 'Escape' });
      });
      expect(grip.getAttribute('aria-pressed')).toBe('false');
      expect(tile.getAttribute('style')).toBe(atRest);
    });

    it('leaves a move in place on Enter, and saves it', async () => {
      saveWallView(GAME, { version: WALL_VIEW_VERSION, widgets: [narrowWidget()] });
      renderWall();

      const tile = screen.getByRole('group');
      const atRest = tile.getAttribute('style');
      const grip = screen.getByRole('button', { name: 'Move Reading' });

      await act(async () => {
        fireEvent.keyDown(grip, { key: 'Enter' });
      });
      await act(async () => {
        fireEvent.keyDown(grip, { key: 'ArrowRight' });
      });
      await act(async () => {
        fireEvent.keyDown(grip, { key: 'Enter' });
      });

      expect(grip.getAttribute('aria-pressed')).toBe('false');
      expect(tile.getAttribute('style')).not.toBe(atRest);

      await settle();
      const saved = loadWallView(GAME);
      const geometry = saved?.widgets[0]?.at?.sm ?? saved?.widgets[0];
      expect(geometry?.x).toBe(1);
    });

    it('commits on blur rather than reverting, the same as a pointer mouse-up', async () => {
      saveWallView(GAME, { version: WALL_VIEW_VERSION, widgets: [narrowWidget()] });
      renderWall();

      const tile = screen.getByRole('group');
      const atRest = tile.getAttribute('style');
      const grip = screen.getByRole('button', { name: 'Move Reading' });

      await act(async () => {
        fireEvent.keyDown(grip, { key: 'Enter' });
      });
      await act(async () => {
        fireEvent.keyDown(grip, { key: 'ArrowRight' });
      });
      await act(async () => {
        fireEvent.blur(grip);
      });

      expect(grip.getAttribute('aria-pressed')).toBe('false');
      expect(tile.getAttribute('style')).not.toBe(atRest);
    });

    it('resizes a tile with the resize handle', async () => {
      saveWallView(GAME, { version: WALL_VIEW_VERSION, widgets: [savedWidget('reading')] });
      renderWall();

      const tile = screen.getByRole('group');
      const atRest = tile.getAttribute('style');
      const handle = screen.getByRole('button', { name: 'Resize' });

      await act(async () => {
        fireEvent.keyDown(handle, { key: 'Enter' });
      });
      expect(handle.getAttribute('aria-pressed')).toBeNull();

      await act(async () => {
        fireEvent.keyDown(handle, { key: 'ArrowDown' });
      });
      await act(async () => {
        fireEvent.keyDown(handle, { key: 'Enter' });
      });

      expect(tile.getAttribute('style')).not.toBe(atRest);

      await settle();
      const saved = loadWallView(GAME);
      const geometry = saved?.widgets[0]?.at?.sm ?? saved?.widgets[0];
      expect(geometry?.h).toBe(7);
    });

    it('names the tile and announces the mode it enters', async () => {
      saveWallView(GAME, { version: WALL_VIEW_VERSION, widgets: [savedWidget('reading')] });
      renderWall();

      expect(screen.getByRole('group', { name: 'Reading' })).toBeTruthy();

      const grip = screen.getByRole('button', { name: 'Move Reading' });
      await act(async () => {
        fireEvent.keyDown(grip, { key: 'Enter' });
      });

      expect(screen.getByText(/Arranging Reading/)).toBeTruthy();
    });
  });
});
