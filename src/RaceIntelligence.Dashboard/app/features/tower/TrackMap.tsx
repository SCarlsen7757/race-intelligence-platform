import { useMemo } from 'react';
import type { TowerRow } from '../../shared/live/contracts';

interface TrackMapProps {
  rows: TowerRow[];
  /** Drivers whose telemetry is open. Labelled on the map, so the two views agree. */
  focusedDriverKeys: readonly string[];
  /** Driver keys whose tower row is expanded. Clicking a dot toggles this, exactly as the row does. */
  expandedDriverKeys: ReadonlySet<string>;
  onSelect: (driverKey: string) => void;
}

/** Radius of the ring itself, as a fraction of the box. Leaves room for dots and labels outside it. */
const RING_RADIUS = 0.34;

/** How far a dot is nudged off the ring to unstack it from a neighbour. */
const JITTER = 0.045;

/** Two cars closer together than this, in lap fraction, are drawn on separate radii. */
const OVERLAP_FRACTION = 0.012;

/** Cars in the pit lane sit on their own inner ring, which is also roughly where a pit lane is. */
const PIT_RADIUS = 0.24;

interface Dot {
  row: TowerRow;
  fraction: number;
  radius: number;
}

/**
 * Where one lap fraction lands on the circle.
 *
 * **Position 1 at the top, running clockwise**, fixed and stated on the figure. A map that reversed
 * itself between sessions would be worse than no map — the whole value is reading it without
 * thinking, and that only works if it is the same picture every time.
 */
function pointAt(fraction: number, radius: number): { x: number; y: number } {
  const angle = fraction * 2 * Math.PI - Math.PI / 2;

  return { x: 0.5 + radius * Math.cos(angle), y: 0.5 + radius * Math.sin(angle) };
}

/**
 * Lays the field out on the ring, nudging cars apart where they would otherwise overlap.
 *
 * Cars bunch on a circle far more than on a real outline: a 7 km circuit puts a two-second gap at
 * about a hundredth of a lap, which on a map-sized ring is less than the width of a dot. Alternating
 * the radius of consecutive close cars keeps both readable and keeps their order along the ring
 * honest, which is the thing being asked.
 */
function layOut(rows: TowerRow[]): Dot[] {
  const placed = rows
    .filter(
      (row): row is TowerRow & { trackPositionFraction: number } =>
        // Omitted rather than drawn at zero, the same rule the timing columns follow: a car whose lap
        // fraction the simulator did not report is not a car sitting on the start line, and drawing it
        // there would invent a battle that is not happening.
        typeof row.trackPositionFraction === 'number',
    )
    .map((row) => ({
      row,
      // Wrapped rather than clamped: a simulator reporting 1.0 at the line means the start, not the
      // finish, and either reading puts the dot in the same place.
      fraction: ((row.trackPositionFraction % 1) + 1) % 1,
    }))
    .sort((a, b) => a.fraction - b.fraction);

  let run = 0;

  return placed.map((dot, index) => {
    const previous = index === 0 ? null : placed[index - 1]!;
    run = previous !== null && dot.fraction - previous.fraction < OVERLAP_FRACTION ? run + 1 : 0;

    if (dot.row.inPitLane === true) {
      return { ...dot, radius: PIT_RADIUS };
    }

    // Alternating out and in from the ring, so a queue of four cars reads as four rather than as a
    // smear. Beyond a few the picture is honest about being crowded, which it is.
    const offset = run === 0 ? 0 : (run % 2 === 1 ? 1 : -1) * JITTER * Math.ceil(run / 2);

    return { ...dot, radius: RING_RADIUS + offset };
  });
}

/**
 * The field as dots on a circle, positioned by lap fraction.
 *
 * A timing tower tells you the order and the gaps. It does not tell you that two cars are about to
 * meet at the same corner, or that the car three places back is on the far side of the circuit and
 * irrelevant for the next ninety seconds. A map answers both at a glance.
 *
 * **Deliberately a circle, not the circuit.** No corner shapes, no elevation, no real geometry — a
 * ring is wrong about the shape of every track and right about the only question being asked, which
 * is who is near whom. Real outlines need either a geometry source the simulator does not publish or
 * a racing line traced from collected samples, and neither should hold up the useful eighty percent.
 *
 * Rendered from the tower snapshot, through React like the tower itself. Standings arrive at a tenth
 * of the focus rate, so the reason `InputsTrace` reaches for canvas — a thousand points redrawn sixty
 * times a second — simply does not apply to twenty circles.
 */
export function TrackMap({ rows, focusedDriverKeys, expandedDriverKeys, onSelect }: TrackMapProps) {
  const dots = useMemo(() => layOut(rows), [rows]);
  const missing = rows.length - dots.length;

  if (rows.length === 0) {
    return null;
  }

  return (
    <figure className="track-map">
      <svg
        viewBox="0 0 1 1"
        role="img"
        aria-label={`Track map: ${dots.length} of ${rows.length} cars positioned around the lap`}
      >
        <circle
          className="track-map__ring"
          cx={0.5}
          cy={0.5}
          r={RING_RADIUS}
          vectorEffect="non-scaling-stroke"
        />

        {/* The start line, so "position 1 at the top" is drawn and not only claimed. */}
        <line
          className="track-map__start"
          x1={0.5}
          y1={0.5 - RING_RADIUS - 0.05}
          x2={0.5}
          y2={0.5 - RING_RADIUS + 0.05}
          vectorEffect="non-scaling-stroke"
        />

        {dots.map(({ row, fraction, radius }) => {
          const { x, y } = pointAt(fraction, radius);
          const isFocused = focusedDriverKeys.includes(row.driverKey);
          const isSelf = row.tier === 'Self';
          const label = row.carNumber != null ? String(row.carNumber) : (row.position ?? '');

          return (
            <g
              key={row.driverKey}
              className={[
                'track-map__car',
                isSelf ? 'track-map__car--self' : '',
                isFocused ? 'track-map__car--focused' : '',
                row.inPitLane === true ? 'track-map__car--pit' : '',
                expandedDriverKeys.has(row.driverKey) ? 'track-map__car--expanded' : '',
              ]
                .filter(Boolean)
                .join(' ')}
              role="button"
              tabIndex={0}
              aria-label={`${row.displayName}, position ${row.position ?? 'unknown'}`}
              onClick={() => onSelect(row.driverKey)}
              onKeyDown={(event) => {
                if (event.key === 'Enter' || event.key === ' ') {
                  event.preventDefault();
                  onSelect(row.driverKey);
                }
              }}
            >
              {/* A native tooltip rather than a hover state in JavaScript: it is the whole of what
                  hovering a dot has to say, and it costs nothing. */}
              <title>{`P${row.position ?? '?'} ${row.displayName}`}</title>

              <circle className="track-map__dot" cx={x} cy={y} r={0.03} />
              <text className="track-map__number" x={x} y={y + 0.011} textAnchor="middle">
                {label}
              </text>
            </g>
          );
        })}
      </svg>

      <figcaption className="track-map__caption">
        Leader at the top, running clockwise.
        {/* Said rather than silently dropped: a field that looks two cars short is a bug report
            waiting to happen, and the honest answer is that those cars reported no lap fraction. */}
        {missing > 0 && ` ${missing} not shown — no lap position reported.`}
      </figcaption>
    </figure>
  );
}
