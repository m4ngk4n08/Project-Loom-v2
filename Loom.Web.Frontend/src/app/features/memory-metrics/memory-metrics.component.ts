import { Component, Input, Output, EventEmitter, ViewChild, ElementRef, AfterViewInit, OnDestroy, inject, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import * as echarts from 'echarts';
import type { EChartsOption } from 'echarts';
import { DashboardTimelineService } from '../../core/services/dashboard-timeline.service';
import { DASHBOARD_CHART_GROUP, connectDashboardChartGroup } from '../../shared/echarts/dashboard-chart-group';
import { LOOM_DARK_THEME_NAME, registerLoomDarkTheme, isLightTheme } from '../../shared/echarts/loom-theme';

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
  @ViewChild('chartElement', { static: false }) chartElement!: ElementRef<HTMLDivElement>;

  private chart: echarts.ECharts | null = null;
  private resizeObserver?: ResizeObserver;
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
      if (this.chart) this.updateChart();
    });
  }

  ngAfterViewInit(): void {
    this.initChart();
    this.updateChart();
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
    this.chart?.dispose();
  }

  private get colors() {
    return isLightTheme() ? this.COLORS.light : this.COLORS.dark;
  }

  private initChart(): void {
    registerLoomDarkTheme();
    this.chart = echarts.init(this.chartElement.nativeElement, LOOM_DARK_THEME_NAME);
    this.chart.group = DASHBOARD_CHART_GROUP;
    connectDashboardChartGroup();

    const colors = this.colors;

    // Static shape set once - per-tick updates only touch xAxis.data/series.data
    // via a merging setOption (default), so tooltip/axisPointer aren't torn down
    // and re-created on every ~1s tick.
    const baseOption: EChartsOption = {
      backgroundColor: 'transparent',
      grid: { left: 8, right: 8, top: 8, bottom: 24, containLabel: true },
      xAxis: {
        type: 'category',
        data: [],
        axisLine: { lineStyle: { color: colors.gridline } },
        axisLabel: { color: colors.textSecondary, fontSize: 11 },
        splitLine: { show: false }
      },
      yAxis: {
        type: 'value',
        min: 0,
        axisLabel: { color: colors.textSecondary, fontSize: 11, formatter: '{value} MB' },
        splitLine: { lineStyle: { color: colors.gridline } }
      },
      tooltip: {
        trigger: 'axis',
        backgroundColor: colors.surface,
        borderColor: colors.gridline,
        textStyle: { color: colors.textPrimary },
        formatter: (params: any) => {
          const p = Array.isArray(params) ? params[0] : params;
          if (p.value == null) return 'Memory: no sample';
          return `Memory: ${Number(p.value).toFixed(1)} MB`;
        }
      },
      series: [{
        type: 'line',
        data: [],
        showSymbol: false,
        smooth: true,
        connectNulls: true,
        lineStyle: { color: colors.series1, width: 2 },
        itemStyle: { color: colors.series1 },
        areaStyle: { color: colors.series1Fill },
        animationDuration: 300,
        animationEasing: 'cubicOut'
      }]
    };
    this.chart.setOption(baseOption);

    this.chart.on('click', (params: any) => {
      if (params.componentType !== 'series' || params.dataIndex == null) return;
      const globalIndex = this.timeline.windowStart() + params.dataIndex;
      const tick = this.timeline.ticks()[globalIndex];
      if (tick) {
        this.pointClick.emit({ timestamp: tick.timestamp, method: 'working-set' });
      }
    });

    this.resizeObserver = new ResizeObserver(() => this.chart?.resize());
    this.resizeObserver.observe(this.chartElement.nativeElement);
  }

  private updateChart(): void {
    if (!this.chart) return;

    const ticks = this.timeline.ticks().slice(
      this.timeline.windowStart(),
      this.timeline.windowEnd()
    );

    this.chart.setOption({
      xAxis: { data: ticks.map(t => t.timestamp.toLocaleTimeString()) },
      series: [{ data: ticks.map(t => t.memory) }]
    });
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
