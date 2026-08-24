import { toUtcIso } from './logs.component';

describe('toUtcIso', () => {
  it('converts a local datetime-local string to a UTC ISO string ending in Z', () => {
    const result = toUtcIso('2026-08-24T14:30');
    expect(result?.endsWith('Z')).toBe(true);
  });

  it('returns undefined for an empty string', () => {
    expect(toUtcIso('')).toBeUndefined();
  });

  it('preserves the instant named in the local timezone', () => {
    const result = toUtcIso('2026-08-24T14:30');
    const expected = new Date(2026, 7, 24, 14, 30).toISOString();
    expect(result).toBe(expected);
  });
});
