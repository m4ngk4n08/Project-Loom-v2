import { CommonModule } from "@angular/common";
import { Component, ElementRef, Input, OnChanges, SimpleChanges, ViewChild } from "@angular/core";
import { Chart, ChartConfiguration, registerables } from "chart.js";


// Register Chart.js component
Chart.register(...registerables);

@Component({
    selector: 'app-cpu-metrics',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './cpu-metrics.component.html',
    styleUrls: ['./cpu-metrics.component.scss']
})
export class CpuMetricsComponent implements OnChanges {
    @Input() data: any = null;
    @ViewChild('chartCanvas', { static: false }) chartCanvas!: ElementRef<HTMLCanvasElement>;

    private chart: Chart | null = null;
    private maxDataPoints = 30; // Keep last 30 data points
    private timestamps: string[] = [];
    private cpuValues: number[] = [];

    // Color pallete
    private readonly COLORS = {
    light: {
      series1: '#2a78d6',
      surface: '#fcfcfb',
      textPrimary: '#0b0b0b',
      textSecondary: '#52514e',
      gridline: '#e1e0d9'
    },
    dark: {
      series1: '#3987e5',
      surface: '#1a1a19',
      textPrimary: '#ffffff',
      textSecondary: '#c3c2b7',
      gridline: '#2c2c2a'
    }
  };

  ngOnChanges(changes: SimpleChanges): void {
      if(changes['data'] && this.data){
        this.updateChart();
      }
  }

  ngAfterViewInit(): void {
    this.initChart();
  }

  ngOnDestroy(): void {
    if(this.chart) {
        this.chart.destroy();
    }
  }

  private initChart(): void {
    const isDark = window.matchMedia('(prefers-color-scheme: dark').matches;
    const colors = isDark ? this.COLORS.dark : this.COLORS.light;

    const config: ChartConfiguration = {
      type: 'line',
      data: {
        labels: this.timestamps,
        datasets: [{
          label: 'CPU Usage %',
          data: this.cpuValues,
          borderColor: colors.series1,
          backgroundColor: 'transparent',
          borderWidth: 2,
          pointRadius: 4,
          pointBackgroundColor: colors.series1,
          pointBorderColor: colors.surface,
          pointBorderWidth: 2,
          tension: 0.4,
          fill: false
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
            max: 100,
            grid: {
              color: colors.gridline,
              lineWidth: 1
            },
            ticks: {
              color: colors.textSecondary,
              font: {
                size: 11
              },
              callback: (value) => `${value}%`
            }
          }
        },
        plugins: {
          legend: {
            display: false // No legend for single series
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
              label: (context) => {
                    return `CPU: ${context.parsed.y?.toFixed(1)}%`;
                }
            }
          }
        }
      }
    };

    this.chart = new Chart(this.chartCanvas.nativeElement, config);
  }

  private updateChart(): void {
    if(!this.chart || !this.data) return;

    // Add new data point
    const timestamp = new Date(this.data.timestamp).toLocaleTimeString();
    this.timestamps.push(timestamp);
    this.cpuValues.push(this.data.cpuUsagePercent);

    // Keep only the last N data points (sliding window)
    if(this.timestamps.length > this.maxDataPoints) {
        this.timestamps.shift();
        this.cpuValues.shift();
    }

    // Update chart
    this.chart.data.labels = this.timestamps;
    this.chart.data.datasets[0].data = this.cpuValues;
    this.chart.update('none') // 'none' = no animation for real-time updates
  }

  get currentCpuUsage(): number {
    return this.data?.cpuUsagePercent ?? 0;
  }
  
  get topHotPaths(): any[] {
    return this.data?.hotpaths?.slice(0, 3) ?? [];
  }
}