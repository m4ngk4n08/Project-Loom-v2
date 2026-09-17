import { memoryUsageRatio } from './memory-usage';

describe('memoryUsageRatio', () => {
  it('divides used by total', () => {
    expect(memoryUsageRatio(50, 100)).toBe(0.5);
  });

  it('returns null when total is zero', () => {
    expect(memoryUsageRatio(0, 0)).toBeNull();
  });

  it('returns null when total is zero but used is not', () => {
    expect(memoryUsageRatio(10, 0)).toBeNull();
  });

  it('returns null when total is NaN', () => {
    expect(memoryUsageRatio(10, NaN)).toBeNull();
  });
});
