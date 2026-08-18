import { describe, expect, it } from 'vitest';
import { readViewFile, serialiseViewFile, viewFileName } from './viewFile';
import { WALL_VIEW_VERSION, type WallView } from './wallView';

const KNOWN = 'tyre-temperature';

/** Every widget id except the one nothing has heard of, which is what an import has to notice. */
const knowsWidget = (widgetId: string) => widgetId !== 'from-the-future';

function wall(widgets: WallView['widgets']): WallView {
  return { version: WALL_VIEW_VERSION, widgets };
}

function widget(widgetId: string, driver?: WallView['widgets'][number]['driver']) {
  return {
    instanceId: `i-${widgetId}`,
    widgetId,
    ...(driver === undefined ? {} : { driver }),
    x: 0,
    y: 0,
    w: 4,
    h: 6,
  };
}

describe('serialiseViewFile', () => {
  it('names the file after the simulator it was arranged for', () => {
    expect(viewFileName('raceroom')).toBe('pitwall-raceroom.json');
  });

  it('writes no driver key, whatever the tiles are bound to', () => {
    const text = serialiseViewFile(
      'raceroom',
      wall([widget(KNOWN, 'selected'), widget('tyre-wear', { slot: 2 })]),
    );

    // The promise the whole binding model exists to keep: a wall is opened against every session of
    // a simulator, so a key written here would name a stranger in the next race. Asserted against
    // the raw text rather than the parsed object, because that is what leaves the machine.
    expect(text).not.toMatch(/id:|slot:\s*"/);
    expect(text).toContain('"selected"');
    expect(text).toContain('"slot": 2');
  });

  it('omits the name entirely when there is not one', () => {
    expect(serialiseViewFile('raceroom', wall([]))).not.toContain('"name"');
    expect(serialiseViewFile('raceroom', wall([]), 'Endurance')).toContain('"name": "Endurance"');
  });
});

describe('readViewFile', () => {
  it('round-trips a wall', () => {
    const original = wall([widget(KNOWN, 'selected'), widget('tyre-wear', { slot: 2 })]);
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

  it('keeps a tile saved without a car rather than refusing the wall', () => {
    // A document from before bindings existed. Dropping it or attaching it to whichever car sits at
    // its old index would both silently edit an arrangement somebody made on purpose.
    const text = JSON.stringify({
      version: WALL_VIEW_VERSION,
      gameKey: 'raceroom',
      widgets: [{ instanceId: 'i', widgetId: KNOWN, driverOrdinal: 0, x: 0, y: 0, w: 4, h: 6 }],
    });

    const result = readViewFile(text, knowsWidget);

    expect(result.ok).toBe(true);
    expect(result.ok && result.view.widgets[0]?.driver).toBeUndefined();
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
