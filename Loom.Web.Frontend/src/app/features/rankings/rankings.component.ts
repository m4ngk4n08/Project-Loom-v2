import { Component, inject, signal, computed, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MetricsExplorerService, MetricSummary } from '../../core/services/metrics-explorer.service';

interface RankedEntry {
  name: string;
  unit: string;
  latest: number;
  average: number;
  min: number;
  max: number;
  p95: number;
  samples: number;
}

type Category = 'hotpath' | 'allocator' | 'thread';

interface RankedCategory {
  key: Category;
  title: string;
  meta: string;
}

@Component({
  selector: 'app-rankings',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './rankings.component.html',
  styleUrls: ['./rankings.component.scss']
})
export class RankingsComponent implements OnInit, OnDestroy {
  private service = inject(MetricsExplorerService);
  private router = inject(Router);
  private refreshTimer: ReturnType<typeof setInterval> | null = null;
  private readonly REFRESH_MS = 2500;
  private readonly TOP_N = 10;

  categories: RankedCategory[] = [
    { key: 'hotpath', title: 'CPU Hotpaths', meta: 'Slowest instrumented methods by average latency' },
    { key: 'allocator', title: 'Top Allocators', meta: 'Largest memory consumers by latest sample' },
    { key: 'thread', title: 'Thread Pressure', meta: 'Thread pool utilization by latest sample' }
  ];

  summaries = signal<MetricSummary[]>([]);
  isLoading = signal(true);
  loadError = signal<string | null>(null);
  lastUpdated = signal<Date | null>(null);

  ranked = computed<Record<Category, RankedEntry[]>>(() => ({
    hotpath: this.buildRanking(this.summaries(), s => this.isHotpath(s.name), (a, b) => b.average - a.average),
    allocator: this.buildRanking(this.summaries(), s => this.isAllocator(s.name), (a, b) => b.latest - a.latest),
    thread: this.buildRanking(this.summaries(), s => s.name.startsWith('threadpool'), (a, b) => b.latest - a.latest)
  }));

  totalMetrics = computed(() => this.summaries().length);

  totalHotpaths = computed(() => this.ranked().hotpath.length);
  totalAllocators = computed(() => this.ranked().allocator.length);
  totalThreads = computed(() => this.ranked().thread.length);

  ngOnInit(): void {
    this.load();
    this.refreshTimer = setInterval(() => this.load(), this.REFRESH_MS);
  }

  ngOnDestroy(): void {
    if (this.refreshTimer) {
      clearInterval(this.refreshTimer);
      this.refreshTimer = null;
    }
  }

  load(): void {
    this.loadError.set(null);
    this.service.getMetricSummary().subscribe({
      next: (summaries) => {
        this.summaries.set(summaries);
        this.isLoading.set(false);
        this.lastUpdated.set(new Date());
      },
      error: (err) => {
        console.error('Failed to load rankings:', err);
        this.isLoading.set(false);
        this.loadError.set('Could not load rankings. Is a target process running?');
      }
    });
  }

  onSelect(name: string): void {
    this.router.navigate(['/metrics'], { queryParams: { metric: name } });
  }

  formatValue(value: number, unit?: string): string {
    if (!Number.isFinite(value)) return '-';
    const formatted = Math.abs(value) >= 1000
      ? value.toLocaleString(undefined, { maximumFractionDigits: 0 })
      : value.toFixed(2);
    return unit ? `${formatted} ${unit}` : formatted;
  }

  formatTime(date: Date | null): string {
    if (!date) return '-';
    return date.toLocaleTimeString();
  }

  private buildRanking(
    summaries: MetricSummary[],
    predicate: (s: MetricSummary) => boolean,
    comparer: (a: RankedEntry, b: RankedEntry) => number
  ): RankedEntry[] {
    return summaries
      .filter(predicate)
      .map(s => ({
        name: s.name,
        unit: s.unit,
        latest: s.latestValue,
        average: s.average,
        min: s.min,
        max: s.max,
        p95: s.p95,
        samples: s.sampleCount
      }))
      .sort(comparer)
      .slice(0, this.TOP_N);
  }

  private isHotpath(name: string): boolean {
    return name.includes('latency') ||
      name.includes('duration') ||
      name.includes('elapsed') ||
      name.includes('.time');
  }

  private isAllocator(name: string): boolean {
    return name.startsWith('gen-') ||
      name.startsWith('loh-') ||
      name.startsWith('poh-') ||
      name === 'working-set' ||
      name === 'gc-heap-size' ||
      name === 'gc-committed' ||
      name === 'gc-fragmentation';
  }
}