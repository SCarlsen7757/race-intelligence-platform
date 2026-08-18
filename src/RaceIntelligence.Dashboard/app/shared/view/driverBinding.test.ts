import { describe, expect, it } from 'vitest';
import { FIRST_SLOT, isDriverBinding, resolveBinding } from './driverBinding';

const FIRST = 'id:4242';
const SECOND = 'slot:7';

describe('resolveBinding', () => {
  it('reads a slot as the car in that position, counting from one', () => {
    expect(resolveBinding({ slot: FIRST_SLOT }, [FIRST, SECOND], null)).toBe(FIRST);
    expect(resolveBinding({ slot: FIRST_SLOT + 1 }, [FIRST, SECOND], null)).toBe(SECOND);
  });

  it('follows the selection', () => {
    expect(resolveBinding('selected', [FIRST, SECOND], SECOND)).toBe(SECOND);
    expect(resolveBinding('selected', [FIRST, SECOND], FIRST)).toBe(FIRST);
  });

  /**
   * A wall saved with three slots, opened against a session where two cars are being watched. The
   * tile has nobody in it, and saying so is the only honest answer — resolving to the nearest car
   * would put a stranger's tyre temperatures under a heading someone trusts.
   */
  it('resolves to nobody rather than to the wrong car', () => {
    expect(resolveBinding({ slot: 3 }, [FIRST, SECOND], FIRST)).toBeUndefined();
    expect(resolveBinding('selected', [], null)).toBeUndefined();
    expect(resolveBinding(undefined, [FIRST], FIRST)).toBeUndefined();
  });
});

describe('isDriverBinding', () => {
  it('accepts the two forms a tile can be bound by', () => {
    expect(isDriverBinding('selected')).toBe(true);
    expect(isDriverBinding({ slot: 1 })).toBe(true);
  });

  /**
   * A slot below one, fractional, or not a number at all indexes nothing. Rejecting it at the door
   * keeps "this slot is empty" meaning what it says, rather than also meaning "this document is
   * damaged".
   */
  it('rejects a slot that could not index a car', () => {
    expect(isDriverBinding({ slot: 0 })).toBe(false);
    expect(isDriverBinding({ slot: -1 })).toBe(false);
    expect(isDriverBinding({ slot: 1.5 })).toBe(false);
    expect(isDriverBinding({ slot: 'first' })).toBe(false);
    expect(isDriverBinding({})).toBe(false);
    expect(isDriverBinding('first')).toBe(false);
    expect(isDriverBinding(null)).toBe(false);
  });
});
