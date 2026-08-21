import { Component, inject, signal, computed, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MetricsExplorerService, MetricSummary } from '../../core/services/metrics-explorer.service';

interface RankedEntry {
  name: string;
  value: number;
  unit: string;
  secondary: string;
}

@Component({
  selector: 'app-top-offenders',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './top-offenders.component.html',
  styleUrls: ['./top-offenders.component.scss']
})
export class TopOffendersComponent implements OnInit, OnDestroy {
  private service = inject(MetricsExplorerService);
  private router = inject(Router);
  private refreshTimer: ReturnType<typeof setInterval> | null = null;
  private readonly REFRESH_MS = 2500;
  private readonly TOP_N = 5;

  summaries = signal<MetricSummary[]>([]);
  isLoading = signal(true);
  loadError = signal<string | null>(null);

  topHotpaths = computed<RankedEntry[]>(() =>
    this.summaries()
      .filter(s => this.isHotpath(s.name))
      .sort((a, b) => b.average - a.average)
      .slice(0, this.TOP_N)
      .map(s => ({
        name: s.name,
        value: s.average,
        unit: s.unit,
        secondary: `p95 ${this.formatValue(s.p95, s.unit)}`
      }))
  );

  topAllocators = computed<RankedEntry[]>(() =>
    this.summaries()
      .filter(s => this.isAllocator(s.name))
      .sort((a, b) => b.latestValue - a.latestValue)
      .slice(0, this.TOP_N)
      .map(s => ({
        name: s.name,
        value: s.latestValue,
        unit: s.unit,
        secondary: `avg ${this.formatValue(s.average, s.unit)}`
      }))
  );

  threadMetrics = computed<RankedEntry[]>(() =>
    this.summaries()
      .filter(s => s.name.startsWith('threadpool'))
      .sort((a, b) => b.latestValue - a.latestValue)
      .map(s => ({
        name: s.name,
        value: s.latestValue,
        unit: s.unit,
        secondary: `avg ${this.formatValue(s.average, s.unit)}`
      }))
  );

  maxValue = computed(() => {
    const entries = [
      ...this.topHotpaths(),
      ...this.topAllocators(),
      ...this.threadMetrics()
    ];
    return entries.length > 0 ? Math.max(...entries.map(e => e.value)) : 0;
  });

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
      },
      error: (err) => {
        console.error('Failed to load top offenders:', err);
        this.isLoading.set(false);
        this.loadError.set('Could not load rankings. Is a target process running?');
      }
    });
  }

  onSelect(name: string): void {
    this.router.navigate(['/metrics'], { queryParams: { metric: name } });
  }

  barWidth(entry: RankedEntry): number {
    return this.maxValue() > 0 ? (entry.value / this.maxValue()) * 100 : 0;
  }

  formatValue(value: number, unit?: string): string {
    if (!Number.isFinite(value)) return '-';
    const formatted = Math.abs(value) >= 1000
      ? value.toLocaleString(undefined, { maximumFractionDigits: 0 })
      : value.toFixed(2);
    return unit ? `${formatted} ${unit}` : formatted;
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
