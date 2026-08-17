import { describe, expect, it } from 'vitest';
import {
  formatDriverKeys,
  MAX_FOCUSED_DRIVERS,
  parseDriverKeys,
  toggleDriverKey,
} from './focusDriverKeys';

describe('focused drivers in the URL', () => {
  it('reads one driver, and two, in the order the path names them', () => {
    expect(parseDriverKeys('id:4242')).toEqual(['id:4242']);
    expect(parseDriverKeys('id:4242,slot:7')).toEqual(['id:4242', 'slot:7']);
  });

  it('round-trips through the path segment', () => {
    const keys = ['id:4242', 'slot:7'];

    expect(parseDriverKeys(formatDriverKeys(keys))).toEqual(keys);
  });

  it('reads nothing from a path with no driver segment', () => {
    expect(parseDriverKeys(undefined)).toEqual([]);
    expect(parseDriverKeys('')).toEqual([]);
  });

  /** A hand-edited URL can name the same driver twice, and two identical columns are not a comparison. */
  it('drops a repeated driver rather than showing them twice', () => {
    expect(parseDriverKeys('id:1,id:1')).toEqual(['id:1']);
  });

  /**
   * The hub refuses more than its cap, so honouring a link that asked for five would show two
   * columns and an error. Capping here means the request is never sent.
   */
  it('caps a link that asks for more drivers than can be followed', () => {
    expect(parseDriverKeys('id:1,id:2,id:3,id:4')).toHaveLength(MAX_FOCUSED_DRIVERS);
  });

  it('adds a driver who is not on screen', () => {
    expect(toggleDriverKey(['id:1'], 'id:2')).toEqual(['id:1', 'id:2']);
  });

  it('removes a driver who is', () => {
    expect(toggleDriverKey(['id:1', 'id:2'], 'id:1')).toEqual(['id:2']);
  });

  /**
   * A comparison is something you sweep through the field with. A button that silently did nothing
   * once two cars were open would read as broken, so the oldest makes way.
   */
  it('drops the driver added longest ago rather than refusing a third', () => {
    expect(toggleDriverKey(['id:1', 'id:2'], 'id:3')).toEqual(['id:2', 'id:3']);
  });
});
