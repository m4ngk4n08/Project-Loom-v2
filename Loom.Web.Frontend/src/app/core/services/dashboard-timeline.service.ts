import { Injectable, inject, signal, effect } from '@angular/core';
import { DashboardStateService } from './dashboard-state.service';

/** A single aligned sample across all dashboard series.
 *  Each metric updates ~1/s at slightly different instants, so ticks are bucketed
 *  by the service and missing series are left null (charts use spanGaps). */
export interface TimelineTick {
  timestamp: Date;
  cpu: number | null;
  memory: number | null;
  active: number | null;
  blocked: number | null;
  waiting: number | null;
}

export const DEFAULT_WINDOW = 30;
export const MAX_TICKS = 180;

/** Single source of truth for the dashboard charts' shared time axis.
 *  Owns the aligned tick buffer and the visible window so all three charts
 *  correlate: brushing the overview scrubs every chart. Cross-chart hover
 *  crosshair sync is handled natively by ECharts (echarts.connect group). */
@Injectable({ providedIn: 'root' })
export class DashboardTimelineService {
  private state = inject(DashboardStateService);

  readonly ticks = signal<TimelineTick[]>([]);
  readonly windowStart = signal(0);
  readonly windowEnd = signal(0);
  readonly following = signal(true);

  constructor() {
    effect(() => {
      const cpu = this.state.cpuData();
      if (cpu) this.record(new Date(cpu.timestamp), 'cpu', cpu.cpuUsagePercent);
    });

    effect(() => {
      const mem = this.state.memoryData();
      if (mem) this.record(new Date(mem.timestamp), 'memory', mem.usedMemoryMb);
    });

    effect(() => {
      const thr = this.state.threadData();
      if (thr) {
        const waiting = Math.max(0, thr.totalThreads - thr.activeThreads - thr.blockedThreads);
        this.record(new Date(thr.timestamp), 'active', thr.activeThreads);
        this.record(new Date(thr.timestamp), 'blocked', thr.blockedThreads);
        this.record(new Date(thr.timestamp), 'waiting', waiting);
      }
    });
  }

  private record(
    timestamp: Date,
    field: 'cpu' | 'memory' | 'active' | 'blocked' | 'waiting',
    value: number
  ): void {
    let newLength = 0;

    this.ticks.update(current => {
      const last = current[current.length - 1];
      let next: TimelineTick[];

      // Same ~1.5s bucket: mutate the last tick instead of appending a sparse row.
      if (last && Math.abs(timestamp.getTime() - last.timestamp.getTime()) < 1500) {
        next = current.slice(0, -1).concat({ ...last, [field]: value });
      } else {
        next = current.concat({
          timestamp,
          cpu: null,
          memory: null,
          active: null,
          blocked: null,
          waiting: null,
          [field]: value
        });
      }

      if (next.length > MAX_TICKS) {
        next = next.slice(next.length - MAX_TICKS);
      }
      newLength = next.length;
      return next;
    });

    if (this.following()) {
      this.windowStart.set(Math.max(0, newLength - DEFAULT_WINDOW));
      this.windowEnd.set(newLength);
    }
  }

  setWindow(start: number, end: number): void {
    const len = this.ticks().length;
    const clampedStart = Math.max(0, Math.min(start, len));
    const clampedEnd = Math.max(clampedStart + 1, Math.min(end, len));
    this.windowStart.set(clampedStart);
    this.windowEnd.set(clampedEnd);
    this.following.set(false);
  }

  followLive(): void {
    const len = this.ticks().length;
    this.windowStart.set(Math.max(0, len - DEFAULT_WINDOW));
    this.windowEnd.set(len);
    this.following.set(true);
  }
}