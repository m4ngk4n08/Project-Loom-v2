import { isSearchableQuery, scoreBarWidth } from './log-search.component';

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
