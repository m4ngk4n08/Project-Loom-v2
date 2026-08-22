import { Component, Input, Output, EventEmitter, ViewChild, ElementRef, AfterViewInit, OnDestroy, inject, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import * as echarts from 'echarts';
import type { EChartsOption } from 'echarts';
import { DashboardTimelineService } from '../../core/services/dashboard-timeline.service';
import { DASHBOARD_CHART_GROUP, connectDashboardChartGroup } from '../../shared/echarts/dashboard-chart-group';
import { LOOM_DARK_THEME_NAME, registerLoomDarkTheme, isLightTheme } from '../../shared/echarts/loom-theme';

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
  @ViewChild('chartElement', { static: false }) chartElement!: ElementRef<HTMLDivElement>;

  private chart: echarts.ECharts | null = null;
  private resizeObserver?: ResizeObserver;
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

    // Static shape set once - per-tick updates only touch xAxis.data/series[].data
    // via a merging setOption (default), so tooltip/legend/axisPointer aren't torn
    // down and re-created on every ~1s tick.
    const baseOption: EChartsOption = {
      backgroundColor: 'transparent',
      grid: { left: 8, right: 8, top: 8, bottom: 48, containLabel: true },
      xAxis: {
        type: 'category',
        data: [],
        axisLine: { show: false },
        axisLabel: { color: colors.textSecondary, fontSize: 11 },
        splitLine: { show: false }
      },
      yAxis: {
        type: 'value',
        axisLabel: { color: colors.textSecondary, fontSize: 11 },
        splitLine: { lineStyle: { color: colors.gridline } }
      },
      tooltip: {
        trigger: 'axis',
        backgroundColor: colors.surface,
        borderColor: colors.gridline,
        textStyle: { color: colors.textPrimary },
        formatter: (params: any) => {
          const list = Array.isArray(params) ? params : [params];
          return list
            .map((p: any) => p.value == null
              ? `${p.seriesName}: no sample`
              : `${p.seriesName}: ${p.value} threads`)
            .join('<br/>');
        }
      },
      legend: {
        bottom: 0,
        textStyle: { color: colors.textPrimary, fontSize: 12 },
        itemWidth: 10,
        itemHeight: 10
      },
      series: [
        {
          name: 'Active',
          type: 'bar',
          stack: 'threads',
          data: [],
          itemStyle: { color: colors.active, borderRadius: 0 }
        },
        {
          name: 'Blocked',
          type: 'bar',
          stack: 'threads',
          data: [],
          itemStyle: { color: colors.blocked, borderRadius: 0 }
        },
        {
          name: 'Waiting',
          type: 'bar',
          stack: 'threads',
          data: [],
          itemStyle: { color: colors.waiting, borderRadius: 0 }
        }
      ]
    };
    this.chart.setOption(baseOption);

    this.chart.on('click', (params: any) => {
      if (params.componentType !== 'series' || params.dataIndex == null) return;
      const globalIndex = this.timeline.windowStart() + params.dataIndex;
      const tick = this.timeline.ticks()[globalIndex];
      if (tick) {
        this.pointClick.emit({ timestamp: tick.timestamp, method: 'threadpool-thread-count' });
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
      series: [
        { data: ticks.map(t => t.active) },
        { data: ticks.map(t => t.blocked) },
        { data: ticks.map(t => t.waiting) }
      ]
    });
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
