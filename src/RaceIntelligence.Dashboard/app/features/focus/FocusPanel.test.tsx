import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { LiveConnection } from '../../shared/live/connection';
import { LiveStore } from '../../shared/live/store';
import { LiveContext } from '../../shared/live/useLive';
import { FocusPanel } from './FocusPanel';

const FIRST_DRIVER = 'id:2';
const SECOND_DRIVER = 'id:9';
const THIRD_DRIVER = 'id:11';

function renderFocus(driverKeys: readonly string[]) {
  const store = new LiveStore();
  store.setFollowedDrivers(driverKeys);

  return render(
    <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
      <FocusPanel
        driverKeys={driverKeys}
        displayName={(driverKey) => driverKey}
        onClose={() => undefined}
      />
    </LiveContext.Provider>,
  );
}

describe('FocusPanel', () => {
  it('gives every driver a column, however many are being watched', () => {
    const { container } = renderFocus([FIRST_DRIVER, SECOND_DRIVER, THIRD_DRIVER]);

    expect(container.querySelectorAll('.focus__section')).toHaveLength(3);
    expect(screen.getAllByRole('heading', { name: 'MoTeC' })).toHaveLength(3);
  });

  /**
   * The grid is told how many columns to draw, rather than a stylesheet assuming two.
   *
   * This is what lets the comparison grow past a pair, and it is only safe because the column now
   * holds readouts rather than charts — a chart at a fifth of the width would not be a chart.
   */
  it('tells the grid how many columns the comparison needs', () => {
    const { container } = renderFocus([FIRST_DRIVER, SECOND_DRIVER, THIRD_DRIVER]);

    const compare = container.querySelector<HTMLElement>('.focus__compare');
    expect(compare?.style.getPropertyValue('--compare-columns')).toBe('3');
  });

  /**
   * One car is not a comparison, so it gets the plain body and no column headings.
   */
  it('lays a single driver out without the comparison grid', () => {
    const { container } = renderFocus([FIRST_DRIVER]);

    expect(container.querySelector('.focus__compare')).toBeNull();
    expect(container.querySelector('.focus__body')).not.toBeNull();
    expect(container.querySelectorAll('.focus__column-name')).toHaveLength(0);
  });

  /**
   * The channels that moved to the pit wall must not also be here.
   *
   * Rendering a stint chart once per car is precisely the cost the strip was cut back to avoid, and
   * it would come back the moment someone re-registered a catalogue panel into these sections.
   */
  it('carries no per-wheel or per-stint channels', () => {
    const { container } = renderFocus([FIRST_DRIVER, SECOND_DRIVER]);

    expect(container.querySelector('.wheel-chart')).toBeNull();
    expect(container.querySelector('.damage')).toBeNull();
    expect(container.querySelector('.trace')).toBeNull();
  });
});
