import { CommonModule } from '@angular/common';
import {
  Component,
  ElementRef,
  ViewChild,
  AfterViewInit,
  OnDestroy,
  inject,
  effect
} from '@angular/core';
import * as echarts from 'echarts';
import type { EChartsOption } from 'echarts';
import { DashboardTimelineService } from '../../core/services/dashboard-timeline.service';
import { LOOM_DARK_THEME_NAME, registerLoomDarkTheme, isLightTheme } from '../../shared/echarts/loom-theme';

@Component({
  selector: 'app-timeline-brush',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './timeline-brush.component.html',
  styleUrls: ['./timeline-brush.component.scss']
})
export class TimelineBrushComponent implements AfterViewInit, OnDestroy {
  @ViewChild('chartElement', { static: false }) chartElement!: ElementRef<HTMLDivElement>;

  timeline = inject(DashboardTimelineService);

  private chart: echarts.ECharts | null = null;
  private resizeObserver?: ResizeObserver;
  // Guards against the 'datazoom' event firing when WE push the range (from
  // timeline state), so it only reacts to the user actually dragging the slider.
  private applyingServiceState = false;

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

  followLive(): void {
    this.timeline.followLive();
  }

  private get colors() {
    return isLightTheme() ? this.COLORS.light : this.COLORS.dark;
  }

  private initChart(): void {
    registerLoomDarkTheme();
    this.chart = echarts.init(this.chartElement.nativeElement, LOOM_DARK_THEME_NAME);

    const colors = this.colors;

    // Static shape set once. Per-tick data updates use a merging setOption
    // (default) and range sync uses dispatchAction rather than setOption -
    // both avoid tearing down the dataZoom slider's zrender elements, which a
    // notMerge:true setOption would do on every tick, aborting an in-progress
    // drag under the user's pointer.
    const baseOption: EChartsOption = {
      backgroundColor: 'transparent',
      animation: false,
      grid: { left: 8, right: 8, top: 8, bottom: 32, containLabel: true },
      xAxis: {
        type: 'category',
        data: [],
        axisLine: { lineStyle: { color: colors.gridline } },
        axisLabel: { show: false },
        splitLine: { show: false }
      },
      yAxis: {
        type: 'value',
        min: 0,
        max: 100,
        show: false
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
      dataZoom: [
        {
          type: 'slider',
          start: 0,
          end: 100,
          height: 20,
          bottom: 4,
          handleSize: '100%',
          borderColor: colors.gridline,
          fillerColor: 'rgba(20, 184, 166, 0.15)',
          dataBackground: {
            lineStyle: { color: colors.series1 },
            areaStyle: { color: colors.series1, opacity: 0.1 }
          },
          textStyle: { color: colors.textSecondary, fontSize: 10 }
        }
      ],
      series: [{
        type: 'line',
        data: [],
        showSymbol: false,
        smooth: true,
        connectNulls: true,
        lineStyle: { color: colors.series1, width: 1.5 },
        itemStyle: { color: colors.series1 }
      }]
    };
    this.chart.setOption(baseOption);

    this.chart.on('datazoom', () => {
      if (this.applyingServiceState || !this.chart) return;
      const option = this.chart.getOption() as any;
      const dz = option.dataZoom?.[0];
      if (!dz) return;
      const count = this.timeline.ticks().length;
      if (count <= 1) return;
      // dataZoom percentages map onto the category axis's index domain
      // [0, count - 1], not [0, count] - matches the write side in updateChart().
      const startIdx = Math.round((dz.start / 100) * (count - 1));
      const endIdx = Math.round((dz.end / 100) * (count - 1)) + 1;
      this.timeline.setWindow(startIdx, endIdx);
    });

    this.resizeObserver = new ResizeObserver(() => this.chart?.resize());
    this.resizeObserver.observe(this.chartElement.nativeElement);
  }

  private updateChart(): void {
    if (!this.chart) return;

    const ticks = this.timeline.ticks();
    const count = ticks.length;
    const startPct = count > 1 ? (this.timeline.windowStart() / (count - 1)) * 100 : 0;
    const endPct = count > 1 ? ((this.timeline.windowEnd() - 1) / (count - 1)) * 100 : 100;

    // Data update: merging setOption (no notMerge) leaves the dataZoom
    // component instance - and any in-progress drag on it - intact.
    this.chart.setOption({
      xAxis: { data: ticks.map(t => t.timestamp.toLocaleTimeString()) },
      series: [{ data: ticks.map(t => t.cpu) }]
    });

    // Range sync: dispatchAction updates the slider's displayed range without
    // recreating it, unlike passing dataZoom.start/end through setOption.
    this.applyingServiceState = true;
    this.chart.dispatchAction({ type: 'dataZoom', start: startPct, end: endPct });
    this.applyingServiceState = false;
  }
}
