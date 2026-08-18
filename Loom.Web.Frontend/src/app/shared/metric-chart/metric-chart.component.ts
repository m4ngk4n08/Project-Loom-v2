import { Component, Input, OnInit, OnDestroy, signal, effect, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import * as echarts from 'echarts';
import type { EChartsOption } from 'echarts';

export type ChartType = 'line' | 'bar' | 'area' | 'gauge' | 'heatmap';

export interface ChartDataPoint {
  timestamp?: Date | string;
  value: number;
  label?: string;
}

@Component({
  selector: 'app-metric-chart',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div
      class="chart-container"
      [attr.role]="'img'"
      [attr.aria-label]="ariaLabel || title + ' chart'"
      [attr.aria-describedby]="chartId + '-desc'">

      <!-- Chart Canvas -->
      <div
        #chartElement
        class="chart"
        [style.height]="height"
        [attr.id]="chartId">
      </div>

      <!-- WCAG: Text alternative for screen readers -->
      <div [attr.id]="chartId + '-desc'" class="sr-only">
        {{ getChartDescription() }}
      </div>

      @if (isLoading()) {
        <div class="chart-loading" role="status" aria-live="polite">
          <span class="loading-spinner" aria-hidden="true"></span>
          <span>Loading chart data...</span>
        </div>
      }
    </div>
  `,
  styles: [`
    .chart-container {
      position: relative;
      width: 100%;
      background: var(--bg-surface);
      border: 1px solid var(--border);
      border-radius: var(--radius-md);
      padding: 1rem;
    }

    .chart {
      width: 100%;
    }

    .chart-loading {
      position: absolute;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 1rem;
      background: rgba(15, 17, 23, 0.9);
      color: var(--text-secondary);
      font-size: 14px;
      border-radius: var(--radius-md);
    }

    .loading-spinner {
      width: 32px;
      height: 32px;
      border: 3px solid var(--border);
      border-top-color: var(--accent);
      border-radius: 50%;
      animation: spin 1s linear infinite;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }

    /* WCAG: Screen reader only content */
    .sr-only {
      position: absolute;
      width: 1px;
      height: 1px;
      padding: 0;
      margin: -1px;
      overflow: hidden;
      clip: rect(0, 0, 0, 0);
      white-space: nowrap;
      border-width: 0;
    }
  `]
})
export class MetricChartComponent implements OnInit, OnDestroy {
  @ViewChild('chartElement', { static: true }) chartElement!: ElementRef<HTMLDivElement>;

  @Input({ required: true }) title!: string;
  @Input({ required: true }) data!: ChartDataPoint[];
  @Input() type: ChartType = 'line';
  @Input() height: string = '300px';
  @Input() ariaLabel?: string;
  @Input() color: string = '#14b8a6'; // Teal accent

  isLoading = signal(false);
  chartId = `chart-${Math.random().toString(36).substr(2, 9)}`;

  private chart?: echarts.ECharts;
  private resizeObserver?: ResizeObserver;

  constructor() {
    // React to data changes
    effect(() => {
      if (this.chart && this.data) {
        this.updateChart();
      }
    });
  }

  ngOnInit(): void {
    this.initChart();
    this.setupResizeObserver();
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
    this.chart?.dispose();
  }

  private initChart(): void {
    // Register dark theme
    echarts.registerTheme('loom-dark', {
      backgroundColor: 'transparent',
      textStyle: {
        color: '#94a3b8' // --text-secondary
      },
      line: {
        smooth: true
      },
      grid: {
        borderColor: 'rgba(255, 255, 255, 0.04)'
      },
      categoryAxis: {
        axisLine: { lineStyle: { color: 'rgba(255, 255, 255, 0.1)' } },
        splitLine: { lineStyle: { color: 'rgba(255, 255, 255, 0.04)' } }
      },
      valueAxis: {
        axisLine: { lineStyle: { color: 'rgba(255, 255, 255, 0.1)' } },
        splitLine: { lineStyle: { color: 'rgba(255, 255, 255, 0.04)' } }
      }
    });

    this.chart = echarts.init(this.chartElement.nativeElement, 'loom-dark');
    this.updateChart();
  }

  private updateChart(): void {
    if (!this.chart) return;

    const option: EChartsOption = {
      title: {
        text: this.title,
        textStyle: {
          color: '#f1f5f9', // --text-primary
          fontSize: 14,
          fontWeight: 600
        },
        left: 0,
        top: 0
      },
      tooltip: {
        trigger: 'axis',
        backgroundColor: '#1a1d26', // --bg-surface
        borderColor: 'rgba(255, 255, 255, 0.06)',
        textStyle: {
          color: '#f1f5f9'
        }
      },
      grid: {
        left: '3%',
        right: '4%',
        bottom: '3%',
        top: '50px',
        containLabel: true
      },
      xAxis: {
        type: 'category',
        data: this.data.map(d => d.label || (d.timestamp ? new Date(d.timestamp).toLocaleTimeString() : '')),
        boundaryGap: this.type === 'bar'
      },
      yAxis: {
        type: 'value'
      },
      series: [
        {
          type: this.type === 'area' ? 'line' : this.type,
          data: this.data.map(d => d.value),
          smooth: true,
          itemStyle: {
            color: this.color
          },
          areaStyle: this.type === 'area' ? {
            color: {
              type: 'linear',
              x: 0,
              y: 0,
              x2: 0,
              y2: 1,
              colorStops: [
                { offset: 0, color: this.color + '80' }, // 50% opacity
                { offset: 1, color: this.color + '00' }  // 0% opacity
              ]
            }
          } : undefined,
          animationDuration: 300,
          animationEasing: 'cubicOut'
        } as any
      ]
    };

    this.chart.setOption(option, true);
  }

  private setupResizeObserver(): void {
    this.resizeObserver = new ResizeObserver(() => {
      this.chart?.resize();
    });

    this.resizeObserver.observe(this.chartElement.nativeElement);
  }

  getChartDescription(): string {
    const dataPoints = this.data.length;
    const minValue = Math.min(...this.data.map(d => d.value));
    const maxValue = Math.max(...this.data.map(d => d.value));
    const avgValue = this.data.reduce((sum, d) => sum + d.value, 0) / dataPoints;

    return `${this.title} ${this.type} chart showing ${dataPoints} data points. ` +
           `Minimum value: ${minValue.toFixed(2)}, ` +
           `Maximum value: ${maxValue.toFixed(2)}, ` +
           `Average value: ${avgValue.toFixed(2)}.`;
  }
}
