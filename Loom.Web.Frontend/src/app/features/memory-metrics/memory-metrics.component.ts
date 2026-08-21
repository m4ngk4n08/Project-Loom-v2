import { Component, Input, Output, EventEmitter, ViewChild, ElementRef, AfterViewInit, OnDestroy, inject, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Chart, ChartConfiguration, registerables } from 'chart.js';
import { DashboardTimelineService } from '../../core/services/dashboard-timeline.service';
import { createChartWithCrosshair, syncCrosshairOnHover } from '../../shared/chart-sync/chart-crosshair.plugin';

Chart.register(...registerables);

@Component({
  selector: 'app-memory-metrics',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './memory-metrics.component.html',
  styleUrls: ['./memory-metrics.component.scss']
})
export class MemoryMetricsComponent implements AfterViewInit, OnDestroy {
  @Input() data: any = null;
  @Output() pointClick = new EventEmitter<{ timestamp: Date; method: string }>();
  @ViewChild('chartCanvas', { static: false }) chartCanvas!: ElementRef<HTMLCanvasElement>;

  private chart: Chart | null = null;
  private timeline = inject(DashboardTimelineService);

  // Color palette (validated from dataviz skill)
  private readonly COLORS = {
    light: {
      series1: '#0d9488',
      series1Fill: 'rgba(13, 148, 136, 0.1)',
      surface: '#ffffff',
      textPrimary: '#0f172a',
      textSecondary: '#475569',
      gridline: '#e2e8f0'
    },
    dark: {
      series1: '#14b8a6',
      series1Fill: 'rgba(20, 184, 166, 0.15)',
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
      type: 'line',
      data: {
        labels: [],
        datasets: [{
          label: 'Memory Usage (MB)',
          data: [],
          borderColor: colors.series1,
          backgroundColor: colors.series1Fill,
          borderWidth: 2,
          pointRadius: 0,
          pointHoverRadius: 5,
          pointBackgroundColor: colors.series1,
          pointBorderColor: colors.surface,
          pointBorderWidth: 2,
          tension: 0.4,
          fill: true, // Area chart - fill under the line
          spanGaps: true
        }]
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
              this.pointClick.emit({ timestamp: tick.timestamp, method: 'working-set' });
            }
          }
        },
        scales: {
          x: {
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
              maxRotation: 0,
              autoSkip: true,
              maxTicksLimit: 6
            }
          },
          y: {
            display: true,
            min: 0,
            grid: {
              color: colors.gridline,
              lineWidth: 1
            },
            ticks: {
              color: colors.textSecondary,
              font: {
                size: 11
              },
              callback: (value: number | string) => `${value} MB`
            }
          }
        },
        plugins: {
          legend: {
            display: false
          },
          tooltip: {
            enabled: true,
            backgroundColor: colors.surface,
            titleColor: colors.textPrimary,
            bodyColor: colors.textSecondary,
            borderColor: colors.gridline,
            borderWidth: 1,
            padding: 12,
            displayColors: false,
            callbacks: {
              label: (context: any) => `Memory: ${context.parsed.y?.toFixed(1)} MB`
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
    this.chart.data.datasets[0].data = ticks.map(t => t.memory);
    this.chart.update('none');
  }

  get currentMemoryUsage(): number {
    return this.data?.usedMemoryMb ?? 0;
  }

  get totalMemory(): number {
    return this.data?.totalMemoryMb ?? 0;
  }

  get gcStats(): any {
    return this.data?.gcStats ?? null;
  }

  get memoryPercentage(): number {
    if (!this.totalMemory) return 0;
    return (this.currentMemoryUsage / this.totalMemory) * 100;
  }
}