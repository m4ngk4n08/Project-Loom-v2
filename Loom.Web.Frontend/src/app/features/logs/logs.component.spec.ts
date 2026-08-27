import { toUtcIso, isSearchableQuery, scoreBarWidth, shortTraceId, matchesTraceFilter } from './logs.component';

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

  it('returns undefined for an unparseable value instead of throwing', () => {
    expect(() => toUtcIso('garbage')).not.toThrow();
    expect(toUtcIso('garbage')).toBeUndefined();
  });
});

describe('isSearchableQuery', () => {
  it('rejects a whitespace-only query', () => {
    expect(isSearchableQuery('   ')).toBe(false);
  });

  it('rejects an empty query', () => {
    expect(isSearchableQuery('')).toBe(false);
  });

  it('accepts a valid query', () => {
    expect(isSearchableQuery('database timeout')).toBe(true);
  });

  it('accepts a query with surrounding whitespace', () => {
    expect(isSearchableQuery('  timeout  ')).toBe(true);
  });
});

describe('scoreBarWidth', () => {
  it('returns 100 for the top score relative to itself', () => {
    expect(scoreBarWidth(5.2, 5.2)).toBe(100);
  });

  it('returns a proportional width below the top score', () => {
    expect(scoreBarWidth(2.6, 5.2)).toBe(50);
  });

  it('does not produce NaN or Infinity when the score is 0', () => {
    const width = scoreBarWidth(0, 5.2);
    expect(Number.isNaN(width)).toBe(false);
    expect(Number.isFinite(width)).toBe(true);
    expect(width).toBe(0);
  });

  it('does not produce NaN or Infinity when the top score is 0', () => {
    const width = scoreBarWidth(0, 0);
    expect(Number.isNaN(width)).toBe(false);
    expect(Number.isFinite(width)).toBe(true);
    expect(width).toBe(0);
  });

  it('clamps to 100 even if a score somehow exceeds the top score', () => {
    expect(scoreBarWidth(10, 5)).toBe(100);
  });
});

describe('shortTraceId', () => {
  it('returns undefined for undefined', () => {
    expect(shortTraceId(undefined)).toBeUndefined();
  });

  it('returns undefined for an empty string', () => {
    expect(shortTraceId('')).toBeUndefined();
  });

  it('returns the first 8 characters of a 32-hex trace id', () => {
    expect(shortTraceId('4bf92f3577b34da6a3ce929d0e0e4736')).toBe('4bf92f35');
  });

  it('returns a value shorter than 8 chars unchanged', () => {
    expect(shortTraceId('abc')).toBe('abc');
  });
});

describe('matchesTraceFilter', () => {
  it('matches everything when the filter is empty, even an undefined id', () => {
    expect(matchesTraceFilter(undefined, '')).toBe(true);
  });

  it('matches everything when the filter is empty, even a populated id', () => {
    expect(matchesTraceFilter('4bf92f3577b34da6a3ce929d0e0e4736', '')).toBe(true);
  });

  it('matches when the id equals the filter', () => {
    expect(matchesTraceFilter('4bf92f3577b34da6a3ce929d0e0e4736', '4bf92f3577b34da6a3ce929d0e0e4736')).toBe(true);
  });

  it('does not match when the id differs from the filter', () => {
    expect(matchesTraceFilter('4bf92f3577b34da6a3ce929d0e0e4736', 'deadbeefdeadbeefdeadbeefdeadbeef')).toBe(false);
  });

  it('does not match a non-empty filter against an undefined id', () => {
    expect(matchesTraceFilter(undefined, '4bf92f3577b34da6a3ce929d0e0e4736')).toBe(false);
  });
});
