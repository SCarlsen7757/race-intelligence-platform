import { describe, expect, it } from 'vitest';
import { MAX_FOCUSED_DRIVERS, toggleDriverKey } from './focusDriverKeys';

const FIRST = 'id:4242';
const SECOND = 'slot:7';
const THIRD = 'id:9';

describe('toggleDriverKey', () => {
  it('adds a driver who is not on screen, and removes one who is', () => {
    expect(toggleDriverKey([], FIRST)).toEqual([FIRST]);
    expect(toggleDriverKey([FIRST], FIRST)).toEqual([]);
    expect(toggleDriverKey([FIRST, SECOND], FIRST)).toEqual([SECOND]);
  });

  /**
   * The cap is the hub's, mirrored here so a viewer never sends a request it knows will be refused.
   * Dropping the oldest rather than refusing the click is what makes sweeping through a field feel
   * like a control rather than like a broken button.
   *
   * This is also the only path by which a car joins the watched set, which is what keeps the follow
   * set inside the cap without anything else having to check.
   */
  it('drops the driver added longest ago rather than refusing the click', () => {
    let keys = [FIRST, SECOND];
    expect(keys).toHaveLength(MAX_FOCUSED_DRIVERS);

    keys = toggleDriverKey(keys, THIRD);

    expect(keys).toHaveLength(MAX_FOCUSED_DRIVERS);
    expect(keys).toContain(THIRD);
    expect(keys).not.toContain(FIRST);
  });
});
