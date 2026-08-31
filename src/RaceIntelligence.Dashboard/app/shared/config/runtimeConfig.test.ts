import { afterEach, describe, expect, it } from 'vitest';
import { readOrigin, readUrl } from '../history/readUrl';
import { DEFAULT_READ_URL } from '../history/readUrlBuild';
import { hubOrigin, hubSocketUrl } from '../live/hubUrl';
import { DEFAULT_HUB_URL } from '../live/hubUrlBuild';

/**
 * The point of moving these origins to runtime is that one built image serves any deployment, so
 * what matters is that an injected value wins and a missing one falls back to what the build
 * substituted — which is what keeps `npm run dev` working with no configuration.
 */
describe('runtime origins', () => {
  afterEach(() => {
    globalThis.__RIP_CONFIG__ = undefined;
  });

  it('falls back to the build-time value when nothing was injected', () => {
    expect(hubOrigin()).toBe(DEFAULT_HUB_URL);
    expect(readOrigin()).toBe(DEFAULT_READ_URL);
  });

  it('prefers an injected origin over the build-time value', () => {
    globalThis.__RIP_CONFIG__ = {
      hubUrl: 'https://race-api.example.com',
      readUrl: 'https://race-read.example.com',
    };

    expect(hubOrigin()).toBe('https://race-api.example.com');
    expect(readOrigin()).toBe('https://race-read.example.com');
  });

  /**
   * A half-injected config is a deployment mistake, not a shape the app should crash on — the
   * build-time value is a better answer than `undefined` reaching a URL constructor.
   */
  it('falls back per field, so one missing origin does not take the other with it', () => {
    globalThis.__RIP_CONFIG__ = { hubUrl: 'https://race-api.example.com' };

    expect(hubOrigin()).toBe('https://race-api.example.com');
    expect(readOrigin()).toBe(DEFAULT_READ_URL);
  });

  it.each([undefined, ''])('treats %o as not configured', (value) => {
    globalThis.__RIP_CONFIG__ = { hubUrl: value };

    expect(hubOrigin()).toBe(DEFAULT_HUB_URL);
  });

  /**
   * The mixed-content trap the issue warns about: the socket scheme comes from the hub's origin,
   * so an injected http:// origin yields ws:// — which is correct behaviour here, and the reason
   * server.mjs warns at startup rather than the browser failing silently later.
   */
  it('derives the socket scheme from the injected origin', () => {
    globalThis.__RIP_CONFIG__ = { hubUrl: 'https://race-api.example.com' };
    expect(hubSocketUrl('/live/view')).toBe('wss://race-api.example.com/live/view');

    globalThis.__RIP_CONFIG__ = { hubUrl: 'http://race-api.example.com' };
    expect(hubSocketUrl('/live/view')).toBe('ws://race-api.example.com/live/view');
  });

  it('builds read URLs against the injected origin', () => {
    globalThis.__RIP_CONFIG__ = { readUrl: 'https://race-read.example.com' };

    expect(readUrl('/api/v1/sessions')).toBe('https://race-read.example.com/api/v1/sessions');
  });
});
