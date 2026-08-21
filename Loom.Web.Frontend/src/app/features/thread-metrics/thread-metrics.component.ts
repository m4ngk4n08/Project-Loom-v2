import { Component, Input, Output, EventEmitter, ViewChild, ElementRef, AfterViewInit, OnDestroy, inject, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Chart, ChartConfiguration, registerables } from 'chart.js';
import { DashboardTimelineService } from '../../core/services/dashboard-timeline.service';
import { createChartWithCrosshair, syncCrosshairOnHover } from '../../shared/chart-sync/chart-crosshair.plugin';

Chart.register(...registerables);

@Component({
  selector: 'app-thread-metrics',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './thread-metrics.component.html',
  styleUrls: ['./thread-metrics.component.scss']
})
export class ThreadMetricsComponent implements AfterViewInit, OnDestroy {
  @Input() data: any = null;
  @Output() pointClick = new EventEmitter<{ timestamp: Date; method: string }>();
  @ViewChild('chartCanvas', { static: false }) chartCanvas!: ElementRef<HTMLCanvasElement>;

  private chart: Chart | null = null;
  private timeline = inject(DashboardTimelineService);

  // Color palette (validated from dataviz skill - categorical)
  private readonly COLORS = {
    light: {
      active: '#3b82f6',    // blue - running threads
      blocked: '#f59e0b',   // amber - blocked threads
      waiting: '#10b981',   // green - waiting threads
      surface: '#ffffff',
      textPrimary: '#0f172a',
      textSecondary: '#475569',
      gridline: '#e2e8f0'
    },
    dark: {
      active: '#3b82f6',    // blue
      blocked: '#f59e0b',   // amber
      waiting: '#10b981',   // green
      surface: '#1a1d26',
      textPrimary: '#f1f5f9',
      textSecondary: '#94a3b8',
      gridline: '#242832'
    }
  };

  constructor() {
    effect(() => {
      this.timeline.ticks();
      this.timeline.windowStart();
      this.timeline.windowEnd();
      this.timeline.crosshairIndex();
      if (this.chart) this.updateChart();
    });
  }

  ngAfterViewInit(): void {
    this.initChart();
    this.updateChart();
  }

  ngOnDestroy(): void {
    if (this.chart) {
      this.chart.destroy();
    }
  }

  private initChart(): void {
    const isDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    const colors = isDark ? this.COLORS.dark : this.COLORS.light;

    const config: ChartConfiguration = {
      type: 'bar',
      data: {
        labels: [],
        datasets: [
          {
            label: 'Active',
            data: [],
            backgroundColor: colors.active,
            borderWidth: 0,
            borderRadius: 4,
            borderSkipped: false
          },
          {
            label: 'Blocked',
            data: [],
            backgroundColor: colors.blocked,
            borderWidth: 0,
            borderRadius: 4,
            borderSkipped: false
          },
          {
            label: 'Waiting',
            data: [],
            backgroundColor: colors.waiting,
            borderWidth: 0,
            borderRadius: 4,
            borderSkipped: false
          }
        ]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: {
          duration: 300
        },
        onHover: syncCrosshairOnHover(this.timeline),
        interaction: {
          mode: 'index',
          intersect: false
        },
        onClick: (_event: any, elements: any[]) => {
          if (elements.length > 0) {
            const globalIndex = this.timeline.windowStart() + elements[0].index;
            const tick = this.timeline.ticks()[globalIndex];
            if (tick) {
              this.pointClick.emit({ timestamp: tick.timestamp, method: 'threadpool-thread-count' });
            }
          }
        },
        scales: {
          x: {
            stacked: true,
            display: true,
            grid: {
              display: false
            },
            ticks: {
              color: colors.textSecondary,
              font: {
                size: 11
              },
              maxRotation: 0,
              autoSkip: true,
              maxTicksLimit: 6
            }
          },
          y: {
            stacked: true,
            display: true,
            grid: {
              color: colors.gridline,
              lineWidth: 1
            },
            ticks: {
              color: colors.textSecondary,
              font: {
                size: 11
              },
              stepSize: 5
            }
          }
        },
        plugins: {
          legend: {
            display: true,
            position: 'bottom',
            labels: {
              color: colors.textPrimary,
              font: {
                size: 12
              },
              padding: 12,
              usePointStyle: true,
              pointStyle: 'circle'
            }
          },
          tooltip: {
            enabled: true,
            backgroundColor: colors.surface,
            titleColor: colors.textPrimary,
            bodyColor: colors.textSecondary,
            borderColor: colors.gridline,
            borderWidth: 1,
            padding: 12,
            callbacks: {
              label: (context: any) => `${context.dataset.label}: ${context.parsed.y} threads`
            }
          }
        }
      }
    };

    this.chart = createChartWithCrosshair(this.chartCanvas.nativeElement, config, this.timeline);
  }

  private updateChart(): void {
    if (!this.chart) return;

    const ticks = this.timeline.ticks().slice(
      this.timeline.windowStart(),
      this.timeline.windowEnd()
    );
    this.chart.data.labels = ticks.map(t => t.timestamp.toLocaleTimeString());
    this.chart.data.datasets[0].data = ticks.map(t => t.active);
    this.chart.data.datasets[1].data = ticks.map(t => t.blocked);
    this.chart.data.datasets[2].data = ticks.map(t => t.waiting);
    this.chart.update('none');
  }

  get totalThreads(): number {
    return this.data?.totalThreads ?? 0;
  }

  get activeThreads(): number {
    return this.data?.activeThreads ?? 0;
  }

  get blockedThreads(): number {
    return this.data?.blockedThreads ?? 0;
  }

  get topBlockages(): any[] {
    return this.data?.blockages?.slice(0, 3) ?? [];
  }
}