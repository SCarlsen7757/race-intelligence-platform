import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { LiveRoomSummary } from '../live/contracts';
import { RoomList } from './RoomList';

function room(overrides: Partial<LiveRoomSummary> = {}): LiveRoomSummary {
  return {
    roomId: 'room-1',
    gameKey: 'raceroom',
    trackName: 'Spa',
    layoutName: 'Grand Prix',
    sessionType: 3,
    driverCount: 12,
    publishers: [
      {
        clientId: 'client-1',
        clientName: 'Gaming PC',
        driverName: 'Mark',
        simDriverId: '4242',
        connectedAtUtc: new Date().toISOString(),
        capabilities: ['TyreWear'],
      },
    ],
    lastUpdatedAtUtc: new Date().toISOString(),
    ...overrides,
  };
}

describe('RoomList', () => {
  it('lists a live session with its track, type and car count', () => {
    render(<RoomList rooms={[room()]} connected onWatch={vi.fn()} />);

    expect(screen.getByText('Spa')).toBeDefined();
    expect(screen.getByText('Grand Prix')).toBeDefined();
    expect(screen.getByText('Race')).toBeDefined();
    expect(screen.getByText('12 cars')).toBeDefined();
  });

  it('prefers the driver name over the machine name', () => {
    render(<RoomList rooms={[room()]} connected onWatch={vi.fn()} />);

    expect(screen.getByText('Mark')).toBeDefined();
    expect(screen.queryByText('Gaming PC')).toBeNull();
  });

  it('falls back to the machine name when the driver is unknown', () => {
    render(
      <RoomList
        rooms={[room({ publishers: [{ ...room().publishers[0]!, driverName: null }] })]}
        connected
        onWatch={vi.fn()}
      />,
    );

    expect(screen.getByText('Gaming PC')).toBeDefined();
  });

  it('opens the session when clicked', () => {
    const onWatch = vi.fn();
    render(<RoomList rooms={[room()]} connected onWatch={onWatch} />);

    screen.getByRole('button').click();

    expect(onWatch).toHaveBeenCalledWith('room-1');
  });

  /**
   * A room whose collectors have all dropped is kept briefly so a reconnect rejoins it rather than
   * starting a new session. Saying so beats an empty space that looks like a rendering bug.
   */
  it('says when a session has no collector attached', () => {
    render(<RoomList rooms={[room({ publishers: [] })]} connected onWatch={vi.fn()} />);

    expect(screen.getByText(/No collector connected/)).toBeDefined();
  });

  it('distinguishes an empty hub from a disconnected dashboard', () => {
    const { rerender } = render(<RoomList rooms={[]} connected onWatch={vi.fn()} />);
    expect(screen.getByText(/Start a collector/)).toBeDefined();

    rerender(<RoomList rooms={[]} connected={false} onWatch={vi.fn()} />);
    expect(screen.getByText(/Waiting for the hub/)).toBeDefined();
  });

  it('uses the singular for a one-car session', () => {
    render(<RoomList rooms={[room({ driverCount: 1 })]} connected onWatch={vi.fn()} />);

    expect(screen.getByText('1 car')).toBeDefined();
  });
});
