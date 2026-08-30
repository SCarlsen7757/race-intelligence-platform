import type {
  OperatingWindowRow,
  RaceRoomSample,
  SlowFrameMessage,
} from '../shared/live/contracts';

/**
 * A slow-channel frame carrying only the channels a test cares about.
 *
 * Every channel is optional on the wire, and absent means "the simulator did not report it" — the
 * same thing it means everywhere else — so a fixture that sets three fields is a valid frame rather
 * than an incomplete one. That is the improvement over the JSON string this replaced: a test used to
 * build a document by hand and nothing checked its shape, so a panel reading a field the fixture
 * spelled differently failed at runtime or, worse, silently rendered a blank.
 */
export function slowFrame(
  driverKey: string,
  sample: RaceRoomSample,
  options: {
    capturedAtUtc?: string;
    roomId?: string;
    operatingWindows?: OperatingWindowRow[];
  } = {},
): SlowFrameMessage {
  return {
    type: 'slowFrame',
    roomId: options.roomId ?? 'room',
    driverKey,
    capturedAtUtc: options.capturedAtUtc ?? '2026-08-16T12:00:00Z',
    sample,
    operatingWindows: options.operatingWindows ?? [],
  };
}

/** One window per corner with the same bounds, which is what a real car reports. */
export function uniformWindows(bounds: {
  tyreOptimal?: number;
  tyreCold?: number;
  tyreHot?: number;
  brakeOptimal?: number;
  brakeCold?: number;
  brakeHot?: number;
  compound?: number;
}): OperatingWindowRow[] {
  // Spread rather than assign, because `exactOptionalPropertyTypes` is on: a key present with the
  // value `undefined` is not the same as an absent key, and the wire omits nulls entirely.
  return [0, 1, 2, 3].map((corner) => ({
    corner,
    ...(bounds.compound === undefined ? {} : { compound: bounds.compound }),
    ...(bounds.tyreOptimal === undefined ? {} : { tyreOptimalCelsius: bounds.tyreOptimal }),
    ...(bounds.tyreCold === undefined ? {} : { tyreColdCelsius: bounds.tyreCold }),
    ...(bounds.tyreHot === undefined ? {} : { tyreHotCelsius: bounds.tyreHot }),
    ...(bounds.brakeOptimal === undefined ? {} : { brakeOptimalCelsius: bounds.brakeOptimal }),
    ...(bounds.brakeCold === undefined ? {} : { brakeColdCelsius: bounds.brakeCold }),
    ...(bounds.brakeHot === undefined ? {} : { brakeHotCelsius: bounds.brakeHot }),
  }));
}
