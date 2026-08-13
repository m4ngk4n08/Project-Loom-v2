import { Component, Input, OnChanges, SimpleChanges, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Chart, ChartConfiguration, registerables } from 'chart.js';

Chart.register(...registerables);

@Component({
  selector: 'app-thread-metrics',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './thread-metrics.component.html',
  styleUrls: ['./thread-metrics.component.scss']
})
export class ThreadMetricsComponent implements OnChanges {
  @Input() data: any = null;
  @ViewChild('chartCanvas', { static: false }) chartCanvas!: ElementRef<HTMLCanvasElement>;

  private chart: Chart | null = null;
  private maxDataPoints = 20;
  private timestamps: string[] = [];
  private activeThreadsData: number[] = [];
  private blockedThreadsData: number[] = [];
  private waitingThreadsData: number[] = [];

  // Color palette (validated from dataviz skill - categorical)
  private readonly COLORS = {
    light: {
      active: '#2a78d6',    // blue - running threads
      blocked: '#eb6834',   // orange - blocked threads
      waiting: '#1baf7a',   // aqua - waiting threads
      surface: '#fcfcfb',
      textPrimary: '#0b0b0b',
      textSecondary: '#52514e',
      gridline: '#e1e0d9'
    },
    dark: {
      active: '#3987e5',    // blue (dark)
      blocked: '#d95926',   // orange (dark)
      waiting: '#199e70',   // aqua (dark)
      surface: '#1a1a19',
      textPrimary: '#ffffff',
      textSecondary: '#c3c2b7',
      gridline: '#2c2c2a'
    }
  };

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['data'] && this.data) {
      this.updateChart();
    }
  }

  ngAfterViewInit(): void {
    this.initChart();
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
        labels: this.timestamps,
        datasets: [
          {
            label: 'Active',
            data: this.activeThreadsData,
            backgroundColor: colors.active,
            borderWidth: 0,
            borderRadius: 4,
            borderSkipped: false
          },
          {
            label: 'Blocked',
            data: this.blockedThreadsData,
            backgroundColor: colors.blocked,
            borderWidth: 0,
            borderRadius: 4,
            borderSkipped: false
          },
          {
            label: 'Waiting',
            data: this.waitingThreadsData,
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
              maxRotation: 0
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
              label: (context) => `${context.dataset.label}: ${context.parsed.y} threads`
            }
          }
        }
      }
    };

    this.chart = new Chart(this.chartCanvas.nativeElement, config);
  }

  private updateChart(): void {
    if (!this.chart || !this.data) return;

    const timestamp = new Date(this.data.timestamp).toLocaleTimeString();
    this.timestamps.push(timestamp);
    this.activeThreadsData.push(this.data.activeThreads);
    this.blockedThreadsData.push(this.data.blockedThreads);

    // Calculate waiting threads (total - active -
    const waitingThreads = this.data.totalThreads - this.data.activeThreads - this.data.blockedThreads;
    this.waitingThreadsData.push(Math.max(0, waitingThreads));

    // Sliding window
    if (this.timestamps.length > this.maxDataPoints) {
      this.timestamps.shift();
      this.activeThreadsData.shift();
      this.blockedThreadsData.shift();
      this.waitingThreadsData.shift();
    }

    this.chart.data.labels = this.timestamps;
    this.chart.data.datasets[0].data = this.activeThreadsData;
    this.chart.data.datasets[1].data = this.blockedThreadsData;
    this.chart.data.datasets[2].data = this.waitingThreadsData;
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