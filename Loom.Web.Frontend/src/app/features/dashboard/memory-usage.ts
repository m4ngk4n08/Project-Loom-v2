export function memoryUsageRatio(usedMb: number, totalMb: number): number | null {
  if (!Number.isFinite(totalMb) || totalMb <= 0) return null;
  return usedMb / totalMb;
}
