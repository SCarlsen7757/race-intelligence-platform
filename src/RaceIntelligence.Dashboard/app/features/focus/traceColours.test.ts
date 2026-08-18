import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

import { TRACE_COLOURS } from './traceColours';

const stylesheet = readFileSync(resolve(__dirname, '../../shared/ui/styles.css'), 'utf8');
const rootBlock = stylesheet.match(/:root\s*{(?<body>[\s\S]*?)}/)?.groups?.body;
const declarations = new Map(
  [...(rootBlock?.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g) ?? [])].map((match) => [
    match[1]!,
    match[2]!.trim(),
  ]),
);

describe('canvas ground colours', () => {
  it('keeps the trace axis on the current muted text ground colour', () => {
    expect(
      TRACE_COLOURS.axis,
      'the chart axis was left painted for the ground colour CSS used to have after the ground moved underneath it',
    ).toBe(declarations.get('--text-muted'));
  });

  it('keeps the trace track on the current hover ground colour', () => {
    expect(
      TRACE_COLOURS.track,
      'the chart track was left painted for the ground colour CSS used to have after the ground moved underneath it',
    ).toBe(declarations.get('--bg-hover'));
  });
});
