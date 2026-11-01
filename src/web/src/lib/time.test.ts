import { describe, expect, it } from 'vitest';
import { relativeTime } from './time';

const now = new Date('2026-11-02T12:00:00Z');
const at = (seconds: number) => new Date(now.getTime() + seconds * 1000);

describe('relative times', () => {
  it.each([
    [-20, 'just now'],
    [45, 'just now'],
    [-130, '2 minutes ago'],
    [-3 * 3600, '3 hours ago'],
    [5 * 3600 - 2, 'in 5 hours'],
    [-26 * 3600, 'yesterday'],
    [3 * 24 * 3600, 'in 3 days'],
    [-15 * 24 * 3600, '2 weeks ago'],
    [-70 * 24 * 3600, '2 months ago'],
    [-400 * 24 * 3600, 'last year'],
  ])('%i seconds from now is "%s"', (seconds, text) => {
    expect(relativeTime(at(seconds), now)).toBe(text);
  });
});
