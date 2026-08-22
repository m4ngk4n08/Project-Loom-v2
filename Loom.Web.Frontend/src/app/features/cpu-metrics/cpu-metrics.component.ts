import { CommonModule } from "@angular/common";
import { Component, ElementRef, Input, Output, EventEmitter, ViewChild, AfterViewInit, OnDestroy, inject, effect } from "@angular/core";
import * as echarts from 'echarts';
import type { EChartsOption } from 'echarts';
import { DashboardTimelineService } from "../../core/services/dashboard-timeline.service";
import { DASHBOARD_CHART_GROUP, connectDashboardChartGroup } from "../../shared/echarts/dashboard-chart-group";
import { LOOM_DARK_THEME_NAME, registerLoomDarkTheme, isLightTheme } from "../../shared/echarts/loom-theme";

@Component({
    selector: 'app-cpu-metrics',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './cpu-metrics.component.html',
    styleUrls: ['./cpu-metrics.component.scss']
})
export class CpuMetricsComponent implements AfterViewInit, OnDestroy {
    @Input() data: any = null;
    @Output() pointClick = new EventEmitter<{ timestamp: Date; method: string }>();
    @ViewChild('chartElement', { static: false }) chartElement!: ElementRef<HTMLDivElement>;

    private chart: echarts.ECharts | null = null;
    private resizeObserver?: ResizeObserver;
    private timeline = inject(DashboardTimelineService);

    // Color pallete
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
    this.resizeObserver?.disconnect();
    this.chart?.dispose();
  }

  private get colors() {
    return isLightTheme() ? this.COLORS.light : this.COLORS.dark;
  }

  private initChart(): void {
    registerLoomDarkTheme();
    this.chart = echarts.init(this.chartElement.nativeElement, LOOM_DARK_THEME_NAME);
    // Linking .group + connect() gives synced tooltips/crosshairs across the
    // CPU/Memory/Thread charts natively - no custom draw plugin needed.
    this.chart.group = DASHBOARD_CHART_GROUP;
    connectDashboardChartGroup();

    const colors = this.colors;

    // Static shape set once - per-tick updates only touch xAxis.data/series.data
    // via a merging setOption (default), so the tooltip/axisPointer components
    // are never torn down and re-created on every ~1s tick.
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
        max: 100,
        axisLabel: { color: colors.textSecondary, fontSize: 11, formatter: '{value}%' },
        splitLine: { lineStyle: { color: colors.gridline } }
      },
      tooltip: {
        trigger: 'axis',
        backgroundColor: colors.surface,
        borderColor: colors.gridline,
        textStyle: { color: colors.textPrimary },
        formatter: (params: any) => {
          const p = Array.isArray(params) ? params[0] : params;
          if (p.value == null) return 'CPU: no sample';
          return `CPU: ${Number(p.value).toFixed(1)}%`;
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
        this.pointClick.emit({ timestamp: tick.timestamp, method: 'cpu-usage' });
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
      series: [{ data: ticks.map(t => t.cpu) }]
    });
  }

  get currentCpuUsage(): number {
    return this.data?.cpuUsagePercent ?? 0;
  }

  get topHotPaths(): any[] {
    return this.data?.hotpaths?.slice(0, 3) ?? [];
  }
}
