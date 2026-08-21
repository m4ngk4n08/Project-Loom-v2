import { Component, Input, OnDestroy, effect, ViewChild, ElementRef, input, afterNextRender } from '@angular/core';
import { CommonModule } from '@angular/common';
import * as echarts from 'echarts';
import type { EChartsOption } from 'echarts';
import { ChartDataPoint } from '../metric-chart/metric-chart.component';

export interface ComparisonSeries {
  name: string;
  data: ChartDataPoint[];
  color: string;
}

@Component({
  selector: 'app-comparison-chart',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div
      class="comparison-chart-container"
      [attr.role]="'img'"
      [attr.aria-label]="ariaLabel || title + ' comparison chart'">

      <div
        #chartElement
        class="comparison-chart"
        [style.height]="height"
        [style.display]="series().length === 0 ? 'none' : 'block'">
      </div>

      @if (series().length === 0) {
        <div class="chart-empty" role="status">
          Add two or more metrics to compare them on one chart
        </div>
      }
    </div>
  `,
  styles: [`
    .comparison-chart-container {
      position: relative;
      width: 100%;
      background: var(--bg-surface);
      border: 1px solid var(--border);
      border-radius: var(--radius-md);
      padding: 1rem;
    }

    .comparison-chart {
      width: 100%;
    }

    .chart-empty {
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: 120px;
      color: var(--text-muted);
      font-size: 13px;
      font-style: italic;
      text-align: center;
    }
  `]
})
export class ComparisonChartComponent implements OnDestroy {
  @ViewChild('chartElement', { static: true }) chartElement!: ElementRef<HTMLDivElement>;

  @Input({ required: true }) title!: string;
  series = input.required<ComparisonSeries[]>();
  @Input() height: string = '320px';
  @Input() ariaLabel?: string;

  private chart?: echarts.ECharts;
  private resizeObserver?: ResizeObserver;

  constructor() {
    effect(() => {
      const seriesData = this.series();
      if (this.chart && seriesData.length > 0) {
        this.updateChart();
      }
    });

    afterNextRender(() => {
      this.initChart();
      this.setupResizeObserver();
      if (this.series().length > 0) {
        this.updateChart();
      }
    });
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
    this.chart?.dispose();
  }

  private initChart(): void {
    echarts.registerTheme('loom-dark', {
      backgroundColor: 'transparent',
      textStyle: { color: '#94a3b8' },
      legend: { textStyle: { color: '#94a3b8' } },
      grid: { borderColor: 'rgba(255, 255, 255, 0.04)' },
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
    if (this.series().length > 0) {
      this.updateChart();
    }
  }

  private updateChart(): void {
    if (!this.chart) return;

    const seriesData = this.series();
    const chartSeries = seriesData.map(s => ({
      name: s.name,
      type: 'line' as const,
      data: s.data.map(d => [this.toTime(d), d.value] as [number, number]),
      smooth: true,
      showSymbol: false,
      lineStyle: { width: 2 },
      itemStyle: { color: s.color },
      areaStyle: {
        opacity: 0.08,
        color: s.color
      }
    }));

    const option: EChartsOption = {
      title: {
        text: this.title,
        textStyle: {
          color: '#f1f5f9',
          fontSize: 14,
          fontWeight: 600
        },
        left: 0,
        top: 0
      },
      legend: {
        top: 0,
        right: 0,
        type: 'scroll',
        itemWidth: 14,
        itemHeight: 8,
        textStyle: { color: '#94a3b8', fontSize: 12 }
      },
      tooltip: {
        trigger: 'axis',
        backgroundColor: '#1a1d26',
        borderColor: 'rgba(255, 255, 255, 0.06)',
        textStyle: { color: '#f1f5f9' },
        valueFormatter: (value) => Number(value).toLocaleString(undefined, { maximumFractionDigits: 2 })
      },
      grid: {
        left: '3%',
        right: '4%',
        bottom: '3%',
        top: '50px',
        containLabel: true
      },
      xAxis: {
        type: 'time'
      },
      yAxis: {
        type: 'value'
      },
      series: chartSeries
    };

    this.chart.setOption(option, true);
    this.chart.resize();
  }

  private toTime(d: ChartDataPoint): number {
    if (d.timestamp) return new Date(d.timestamp).getTime();
    return Number(d.label ?? Date.now());
  }

  private setupResizeObserver(): void {
    this.resizeObserver = new ResizeObserver(() => {
      this.chart?.resize();
    });
    this.resizeObserver.observe(this.chartElement.nativeElement);
  }
}