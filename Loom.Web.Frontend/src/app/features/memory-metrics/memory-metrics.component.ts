import { Component, Input, OnChanges, SimpleChanges, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Chart, ChartConfiguration, registerables } from 'chart.js';

Chart.register(...registerables);

@Component({
  selector: 'app-memory-metrics',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './memory-metrics.component.html',
  styleUrls: ['./memory-metrics.component.scss']
})
export class MemoryMetricsComponent implements OnChanges {
  @Input() data: any = null;
  @ViewChild('chartCanvas', { static: false }) chartCanvas!: ElementRef<HTMLCanvasElement>;

  private chart: Chart | null = null;
  private maxDataPoints = 30;
  private timestamps: string[] = [];
  private memoryValues: number[] = [];

  // Color palette (validated from dataviz skill)
  private readonly COLORS = {
    light: {
      series1: '#2a78d6',
      series1Fill: 'rgba(42, 120, 214, 0.1)',
      surface: '#fcfcfb',
      textPrimary: '#0b0b0b',
      textSecondary: '#52514e',
      gridline: '#e1e0d9'
    },
    dark: {
      series1: '#3987e5',
      series1Fill: 'rgba(57, 135, 229, 0.15)',
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
      type: 'line',
      data: {
        labels: this.timestamps,
        datasets: [{
          label: 'Memory Usage (MB)',
          data: this.memoryValues,
          borderColor: colors.series1,
          backgroundColor: colors.series1Fill,
          borderWidth: 2,
          pointRadius: 4,
          pointBackgroundColor: colors.series1,
          pointBorderColor: colors.surface,
          pointBorderWidth: 2,
          tension: 0.4,
          fill: true // Area chart - fill under the line
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: {
          duration: 300
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
              maxRotation: 0
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
              callback: (value) => `${value} MB`
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
              label: (context) => `Memory: ${context.parsed.toFixed(1)} MB`
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
    this.memoryValues.push(this.data.usedMemoryMb);

    // Sliding window
    if (this.timestamps.length > this.maxDataPoints) {
      this.timestamps.shift();
      this.memoryValues.shift();
    }

    this.chart.data.labels = this.timestamps;
    this.chart.data.datasets[0].data = this.memoryValues;
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