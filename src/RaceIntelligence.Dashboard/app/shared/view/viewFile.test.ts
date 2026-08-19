import { describe, expect, it } from 'vitest';
import { readViewFile, serialiseViewFile, viewFileName, type ViewFile } from './viewFile';
import { WALL_VIEW_VERSION, type WallView } from './wallView';

const KNOWN = 'tyre-temperature';

/** Every widget id except the one nothing has heard of, which is what an import has to notice. */
const knowsWidget = (widgetId: string) => widgetId !== 'from-the-future';

function wall(widgets: WallView['widgets']): WallView {
  return { version: WALL_VIEW_VERSION, widgets };
}

function widget(widgetId: string) {
  return { instanceId: `i-${widgetId}`, widgetId, x: 0, y: 0, w: 4, h: 6 };
}

describe('serialiseViewFile', () => {
  it('names the file after the simulator it was arranged for', () => {
    expect(viewFileName('raceroom')).toBe('pitwall-raceroom.json');
  });

  it('names no car in any form', () => {
    // A wall is opened against every session of a simulator, so anything here that pointed at a car
    // would point at a stranger in the next race. Asserted against the raw text rather than the
    // parsed object, because the text is what leaves the machine.
    const text = serialiseViewFile('raceroom', wall([widget(KNOWN), widget('tyre-wear')]));

    expect(text).not.toMatch(/driver|slot|selected|id:/);

    const written = JSON.parse(text) as ViewFile;
    expect(Object.keys(written.widgets[0] ?? {})).toEqual([
      'instanceId',
      'widgetId',
      'x',
      'y',
      'w',
      'h',
    ]);
  });

  it('strips a binding written by an older build rather than passing it through', () => {
    // The tile is kept — it is a perfectly good placement — but the dead field must not survive
    // into a document that outlives this build, or exported walls would carry a reference to a car
    // forever.
    const text = serialiseViewFile(
      'raceroom',
      wall([{ ...widget(KNOWN), driver: 'selected' } as WallView['widgets'][number]]),
    );

    expect(text).not.toContain('selected');
  });

  it('omits the name entirely when there is not one', () => {
    expect(serialiseViewFile('raceroom', wall([]))).not.toContain('"name"');
    expect(serialiseViewFile('raceroom', wall([]), 'Endurance')).toContain('"name": "Endurance"');
  });
});

describe('readViewFile', () => {
  it('round-trips a wall', () => {
    const original = wall([widget(KNOWN), widget('tyre-wear')]);
    const result = readViewFile(serialiseViewFile('raceroom', original), knowsWidget);

    expect(result.ok).toBe(true);
    if (!result.ok) return;

    expect(result.view).toEqual(original);
    expect(result.dropped).toEqual([]);
    expect(result.gameKey).toBe('raceroom');
  });

  it('drops a widget this build has never heard of, and names it', () => {
    const text = serialiseViewFile('raceroom', wall([widget(KNOWN), widget('from-the-future')]));
    const result = readViewFile(text, knowsWidget);

    expect(result.ok).toBe(true);
    if (!result.ok) return;

    expect(result.view.widgets.map((w) => w.widgetId)).toEqual([KNOWN]);
    expect(result.dropped).toEqual(['from-the-future']);
  });

  it('names an unknown widget once however many times it was placed', () => {
    const text = serialiseViewFile(
      'raceroom',
      wall([
        { ...widget('from-the-future'), instanceId: 'a' },
        { ...widget('from-the-future'), instanceId: 'b' },
      ]),
    );
    const result = readViewFile(text, knowsWidget);

    expect(result.ok && result.dropped).toEqual(['from-the-future']);
  });

  it('reports the simulator a file came from without judging it', () => {
    // Whether this is a mismatch is the caller's question — only the caller knows the room.
    const result = readViewFile(serialiseViewFile('iracing', wall([widget(KNOWN)])), knowsWidget);

    expect(result.ok && result.gameKey).toBe('iracing');
  });

  it.each([
    ['an ordinal', { driverOrdinal: 0 }],
    ['a selected binding', { driver: 'selected' }],
    ['a slot binding', { driver: { slot: 2 } }],
  ])('keeps a tile bound by %s, and drops the binding', (_label, binding) => {
    // Every shape a binding has ever had. The placement is somebody's arrangement and is kept;
    // the field is dead and must not be carried back out on the next export.
    const text = JSON.stringify({
      version: WALL_VIEW_VERSION,
      gameKey: 'raceroom',
      widgets: [{ instanceId: 'i', widgetId: KNOWN, ...binding, x: 0, y: 0, w: 4, h: 6 }],
    });

    const result = readViewFile(text, knowsWidget);

    expect(result.ok).toBe(true);
    expect(result.ok && result.view.widgets).toHaveLength(1);
    expect(result.ok && Object.keys(result.view.widgets[0] ?? {})).not.toContain('driver');
    expect(result.ok && Object.keys(result.view.widgets[0] ?? {})).not.toContain('driverOrdinal');
  });

  it('refuses what is not JSON', () => {
    const result = readViewFile('{ not json', knowsWidget);

    expect(result.ok).toBe(false);
    expect(result.ok === false && result.reason).toMatch(/not JSON/);
  });

  it.each([
    ['an array', '[]'],
    ['a bare number', '42'],
    ['an object that is not a wall', '{"hello":"world"}'],
  ])('refuses %s', (_label, text) => {
    expect(readViewFile(text, knowsWidget).ok).toBe(false);
  });

  it('refuses a format version it does not read, and says which', () => {
    const text = JSON.stringify({ version: 99, gameKey: 'raceroom', widgets: [] });
    const result = readViewFile(text, knowsWidget);

    expect(result.ok).toBe(false);
    expect(result.ok === false && result.reason).toContain('99');
  });

  it('refuses a wall whose widget is structurally wrong', () => {
    const text = JSON.stringify({
      version: WALL_VIEW_VERSION,
      gameKey: 'raceroom',
      widgets: [{ instanceId: 'i', widgetId: KNOWN, x: 'left', y: 0, w: 4, h: 6 }],
    });

    expect(readViewFile(text, knowsWidget).ok).toBe(false);
  });
});
