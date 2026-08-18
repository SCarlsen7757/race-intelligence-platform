import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { LiveConnection } from '../../shared/live/connection';
import { LiveStore } from '../../shared/live/store';
import { LiveContext } from '../../shared/live/useLive';
import { registerSimPanels, type SimPanel } from '../../sims/registry';
import { FocusPanel } from './FocusPanel';

const FIRST_DRIVER = 'id:2';
const SECOND_DRIVER = 'id:9';

function emptyAwarePanel(): SimPanel {
  return {
    id: 'optional-channel',
    title: 'Optional channel',
    requires: ['OptionalChannel'],
    component: ({ driverKey }) => <span data-testid="optional-reading">{driverKey}</span>,
    isEmpty: (extras) => extras?.extras === 'empty',
  };
}

function renderFocus(
  driverKeys: readonly string[],
  extrasByDriver: Readonly<Record<string, string>>,
) {
  const store = new LiveStore();
  store.setFollowedDrivers(driverKeys);

  for (const [driverKey, extras] of Object.entries(extrasByDriver)) {
    store.apply({
      type: 'extrasFrame',
      roomId: 'room',
      driverKey,
      capturedAtUtc: '2026-08-18T12:00:00Z',
      extras,
    });
  }

  return render(
    <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
      <FocusPanel
        driverKeys={driverKeys}
        gameKey="focus-empty-test"
        capabilities={['OptionalChannel']}
        displayName={(driverKey) => driverKey}
        onClose={() => undefined}
      />
    </LiveContext.Provider>,
  );
}

describe('FocusPanel optional sections', () => {
  it('omits a panel heading and frame when every displayed driver has nothing to show', () => {
    registerSimPanels('focus-empty-test', [emptyAwarePanel()]);

    const { container } = renderFocus([FIRST_DRIVER], { [FIRST_DRIVER]: 'empty' });

    expect(screen.queryByRole('heading', { name: 'Optional channel' })).toBeNull();
    expect(container.querySelector('.focus__section--chart')).toBeNull();
  });

  it('keeps both sides of a comparison when only one driver has something to show', () => {
    registerSimPanels('focus-empty-test', [emptyAwarePanel()]);

    renderFocus([FIRST_DRIVER, SECOND_DRIVER], {
      [FIRST_DRIVER]: 'empty',
      [SECOND_DRIVER]: 'reported',
    });

    expect(screen.getAllByRole('heading', { name: 'Optional channel' })).toHaveLength(2);
    expect(screen.getAllByTestId('optional-reading').map((reading) => reading.textContent)).toEqual(
      [FIRST_DRIVER, SECOND_DRIVER],
    );
  });
});
