import { CommonModule } from '@angular/common';
import {
  Component,
  ElementRef,
  ViewChild,
  AfterViewInit,
  OnDestroy,
  inject,
  signal,
  effect
} from '@angular/core';
import { Chart, ChartConfiguration, registerables } from 'chart.js';
import { DashboardTimelineService } from '../../core/services/dashboard-timeline.service';
import { createChartWithCrosshair, syncCrosshairOnHover } from '../../shared/chart-sync/chart-crosshair.plugin';

Chart.register(...registerables);

type DragMode = 'move' | 'resize-left' | 'resize-right' | 'create' | null;

@Component({
  selector: 'app-timeline-brush',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './timeline-brush.component.html',
  styleUrls: ['./timeline-brush.component.scss']
})
export class TimelineBrushComponent implements AfterViewInit, OnDestroy {
  @ViewChild('brushCanvas', { static: false }) brushCanvas!: ElementRef<HTMLCanvasElement>;
  @ViewChild('brushOverlay', { static: false }) brushOverlay!: ElementRef<HTMLDivElement>;

  timeline = inject(DashboardTimelineService);

  // Band geometry (pixels, relative to the chart plot area)
  readonly bandLeft = signal(0);
  readonly bandWidth = signal(0);
  readonly overlayTop = signal(0);
  readonly overlayHeight = signal(0);

  private chart: Chart | null = null;
  private dragMode: DragMode = null;
  private dragAnchorIndex = 0;
  private dragStartWindowStart = 0;
  private dragStartWindowEnd = 0;

  private readonly COLORS = {
    light: {
      series1: '#0d9488',
      surface: '#ffffff',
      textPrimary: '#0f172a',
      textSecondary: '#475569',
      gridline: '#e2e8f0'
    },
    dark: {
      series1: '#14b8a6',
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
      if (this.chart) this.updateChart();
    });
  }

  ngAfterViewInit(): void {
    this.initChart();
    this.updateChart();
  }

  ngOnDestroy(): void {
    if (this.chart) this.chart.destroy();
  }

  followLive(): void {
    this.timeline.followLive();
  }

  private initChart(): void {
    const isDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    const colors = isDark ? this.COLORS.dark : this.COLORS.light;

    const config: ChartConfiguration = {
      type: 'line',
      data: {
        labels: [],
        datasets: [{
          label: 'CPU Usage %',
          data: [],
          borderColor: colors.series1,
          backgroundColor: 'transparent',
          borderWidth: 1.5,
          pointRadius: 0,
          pointHoverRadius: 4,
          tension: 0.3,
          fill: false,
          spanGaps: true
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: { duration: 0 },
        onHover: syncCrosshairOnHover(this.timeline, () => 0),
        scales: {
          x: {
            type: 'linear',
            min: -0.5,
            max: 0.5,
            display: true,
            grid: { display: false },
            ticks: {
              color: colors.textSecondary,
              font: { size: 10 },
              maxRotation: 0,
              autoSkip: true,
              maxTicksLimit: 8,
              callback: (value: number | string) => {
                const index = Math.round(Number(value) + 0.5);
                const ticks = this.timeline.ticks();
                return (index >= 0 && index < ticks.length)
                  ? ticks[index].timestamp.toLocaleTimeString()
                  : '';
              }
            }
          },
          y: {
            display: false,
            min: 0,
            max: 100
          }
        },
        plugins: {
          legend: { display: false },
          tooltip: {
            enabled: true,
            mode: 'index',
            intersect: false,
            backgroundColor: colors.surface,
            titleColor: colors.textPrimary,
            bodyColor: colors.textSecondary,
            borderColor: colors.gridline,
            borderWidth: 1,
            padding: 10,
            displayColors: false,
            callbacks: {
              label: (context: any) => `CPU: ${context.parsed.y?.toFixed(1)}%`
            }
          }
        }
      }
    };

    this.chart = createChartWithCrosshair(this.brushCanvas.nativeElement, config, this.timeline, () => 0);
  }

  private updateChart(): void {
    if (!this.chart) return;

    const ticks = this.timeline.ticks();
    const count = ticks.length;
    const xScale = this.chart.scales['x'] as any;
    if (xScale && xScale.options) {
      xScale.options.min = -0.5;
      xScale.options.max = Math.max(0.5, count - 0.5);
    }

    this.chart.data.labels = ticks.map(t => t.timestamp.toLocaleTimeString());
    this.chart.data.datasets[0].data = ticks.map(t => t.cpu);
    this.chart.update('none');

    this.updateBand();
  }

  private updateBand(): void {
    if (!this.chart) return;
    const area = this.chart.chartArea;
    const start = this.timeline.windowStart();
    const end = this.timeline.windowEnd();

    this.overlayTop.set(area.top);
    this.overlayHeight.set(area.bottom - area.top);

    const xScale = this.chart.scales['x'] as any;
    const leftPx = xScale.getPixelForValue(start);
    const rightPx = xScale.getPixelForValue(Math.max(end - 1, start));
    this.bandLeft.set(leftPx - area.left);
    this.bandWidth.set(Math.max(rightPx - leftPx, 8));
  }

  private pixelToIndex(clientX: number): number {
    const rect = this.brushOverlay.nativeElement.getBoundingClientRect();
    const xScale = this.chart?.scales['x'] as any;
    if (!xScale) return 0;
    const pixel = clientX - rect.left;
    const value = xScale.getValueForPixel(pixel);
    return Math.round(value + 0.5);
  }

  onPointerDown(event: PointerEvent): void {
    const target = event.target as HTMLElement;
    const handle = target.closest('.brush-handle');
    const band = target.closest('.brush-band');

    this.dragAnchorIndex = this.pixelToIndex(event.clientX);
    this.dragStartWindowStart = this.timeline.windowStart();
    this.dragStartWindowEnd = this.timeline.windowEnd();

    if (handle) {
      this.dragMode = handle.classList.contains('brush-handle--left')
        ? 'resize-left'
        : 'resize-right';
    } else if (band) {
      this.dragMode = 'move';
    } else {
      this.dragMode = 'create';
    }

    event.preventDefault();
    this.brushOverlay.nativeElement.setPointerCapture?.(event.pointerId);
  }

  onPointerMove(event: PointerEvent): void {
    if (!this.dragMode || !this.chart) return;

    const count = this.timeline.ticks().length;
    const index = this.pixelToIndex(event.clientX);

    switch (this.dragMode) {
      case 'create': {
        const anchor = this.dragAnchorIndex;
        const start = Math.max(0, Math.min(anchor, index));
        const end = Math.min(count, Math.max(anchor, index) + 1);
        this.timeline.setWindow(start, end);
        break;
      }
      case 'move': {
        const delta = index - this.dragAnchorIndex;
        const width = this.dragStartWindowEnd - this.dragStartWindowStart;
        const start = Math.max(0, Math.min(this.dragStartWindowStart + delta, count - width));
        this.timeline.setWindow(start, start + width);
        break;
      }
      case 'resize-left': {
        const end = this.dragStartWindowEnd;
        const start = Math.max(0, Math.min(index, end - 1));
        this.timeline.setWindow(start, end);
        break;
      }
      case 'resize-right': {
        const start = this.dragStartWindowStart;
        const end = Math.min(count, Math.max(index + 1, start + 1));
        this.timeline.setWindow(start, end);
        break;
      }
    }
  }

  onPointerUp(event: PointerEvent): void {
    this.dragMode = null;
    this.brushOverlay.nativeElement.releasePointerCapture?.(event.pointerId);
  }
}