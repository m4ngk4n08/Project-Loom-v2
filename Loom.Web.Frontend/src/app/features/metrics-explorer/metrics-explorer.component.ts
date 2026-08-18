import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MetricsExplorerService } from '../../core/services/metrics-explorer.service';
import { MetricChartComponent, ChartDataPoint } from '../../shared/metric-chart/metric-chart.component';
import { DataTableComponent, TableColumn, TableRow } from '../../shared/data-table/data-table.component';

@Component({
  selector: 'app-metrics-explorer',
  standalone: true,
  imports: [CommonModule, FormsModule, MetricChartComponent, DataTableComponent],
  template: `
    <div class="metrics-explorer-page">
      <header class="page-header">
        <h1 class="page-title">Metrics Explorer</h1>
        <p class="page-subtitle">Browse and analyze all available metrics</p>
      </header>

      <div class="explorer-layout">
        <!-- Metrics List (Left Panel) -->
        <aside class="metrics-list surface" role="complementary" aria-label="Available metrics">
          <div class="list-header">
            <h2 class="list-title">Available Metrics</h2>
            <span class="metrics-count" [attr.aria-label]="metricNames().length + ' metrics available'">
              {{ metricNames().length }}
            </span>
          </div>

          @if (isLoadingNames()) {
            <div class="list-loading" role="status" aria-live="polite">
              <span class="loading-spinner" aria-hidden="true"></span>
              <span>Loading metrics...</span>
            </div>
          } @else if (metricNames().length === 0) {
            <div class="list-empty">
              <p>No metrics available</p>
            </div>
          } @else {
            <ul class="metric-items" role="list">
              @for (name of metricNames(); track name) {
                <li class="metric-item">
                  <button
                    class="metric-button"
                    [class.active]="selectedMetric() === name"
                    (click)="selectMetric(name)"
                    [attr.aria-label]="'Select ' + name + ' metric'"
                    [attr.aria-pressed]="selectedMetric() === name"
                    type="button">
                    <span class="metric-name">{{ name }}</span>
                  </button>
                </li>
              }
            </ul>
          }
        </aside>

        <!-- Metric Details (Right Panel) -->
        <main class="metric-details" role="main">
          @if (!selectedMetric()) {
            <div class="empty-state">
              <p>Select a metric from the list to view its data</p>
            </div>
          } @else {
            <div class="details-content">
              <div class="details-header">
                <h2 class="metric-title">{{ selectedMetric() }}</h2>
                <div class="lookback-selector" role="group" aria-label="Time range selection">
                  <label for="lookback-select" class="sr-only">Select time range</label>
                  <select
                    id="lookback-select"
                    class="lookback-select"
                    [(ngModel)]="lookback"
                    (ngModelChange)="loadMetricData()"
                    [attr.aria-label]="'Time range: ' + lookback">
                    <option value="5m">Last 5 minutes</option>
                    <option value="15m">Last 15 minutes</option>
                    <option value="1h">Last hour</option>
                    <option value="6h">Last 6 hours</option>
                    <option value="24h">Last 24 hours</option>
                  </select>
                </div>
              </div>

              @if (isLoadingData()) {
                <div class="data-loading" role="status" aria-live="polite">
                  <span class="loading-spinner" aria-hidden="true"></span>
                  <span>Loading metric data...</span>
                </div>
              } @else if (chartData().length > 0) {
                <div class="metric-visualization">
                  <app-metric-chart
                    [title]="selectedMetric() || ''"
                    [data]="chartData()"
                    type="area"
                    height="350px"
                  />
                </div>

                <div class="metric-table">
                  <h3 class="table-heading">Raw Data</h3>
                  <app-data-table
                    [columns]="tableColumns"
                    [data]="tableData()"
                    emptyMessage="No data available for this time range"
                    ariaLabel="Metric data table"
                  />
                </div>
              } @else {
                <div class="empty-state">
                  <p>No data available for this metric in the selected time range</p>
                </div>
              }
            </div>
          }
        </main>
      </div>
    </div>
  `,
  styles: [`
    .metrics-explorer-page {
      display: flex;
      flex-direction: column;
      gap: 1.5rem;
      height: 100%;
    }

    .page-header {
      .page-title {
        font-size: 28px;
        font-weight: 700;
        color: var(--text-primary);
        margin: 0 0 0.5rem;
      }

      .page-subtitle {
        font-size: 14px;
        color: var(--text-secondary);
        margin: 0;
      }
    }

    .explorer-layout {
      display: grid;
      grid-template-columns: 300px 1fr;
      gap: 1.5rem;
      height: calc(100vh - 200px);

      @media (max-width: 1024px) {
        grid-template-columns: 1fr;
        height: auto;
      }
    }

    .metrics-list {
      padding: 1rem;
      overflow-y: auto;
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }

    .list-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding-bottom: 0.75rem;
      border-bottom: 1px solid var(--border);
    }

    .list-title {
      font-size: 14px;
      font-weight: 600;
      color: var(--text-primary);
      margin: 0;
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    .metrics-count {
      background: var(--bg-elevated);
      color: var(--accent);
      padding: 0.25rem 0.5rem;
      border-radius: 4px;
      font-size: 12px;
      font-weight: 600;
    }

    .metric-items {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }

    .metric-item {
      margin: 0;
    }

    .metric-button {
      width: 100%;
      text-align: left;
      padding: 0.75rem;
      background: transparent;
      border: 1px solid transparent;
      border-radius: var(--radius-sm);
      color: var(--text-secondary);
      font-size: 13px;
      cursor: pointer;
      transition: all var(--transition);

      &:hover {
        background: rgba(20, 184, 166, 0.1);
        color: var(--accent);
        border-color: rgba(20, 184, 166, 0.3);
      }

      &:focus-visible {
        outline: 2px solid var(--accent);
        outline-offset: 2px;
      }

      &.active {
        background: rgba(20, 184, 166, 0.15);
        color: var(--accent);
        font-weight: 500;
        border-color: var(--accent);
      }
    }

    .metric-name {
      display: block;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .metric-details {
      display: flex;
      flex-direction: column;
      overflow-y: auto;
    }

    .details-content {
      display: flex;
      flex-direction: column;
      gap: 1.5rem;
    }

    .details-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 1rem;
      flex-wrap: wrap;
    }

    .metric-title {
      font-size: 20px;
      font-weight: 600;
      color: var(--text-primary);
      margin: 0;
      font-family: 'Monaco', 'Courier New', monospace;
    }

    .lookback-select {
      background: var(--bg-surface);
      color: var(--text-primary);
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      padding: 0.5rem 0.75rem;
      font-size: 13px;
      cursor: pointer;
      transition: all var(--transition);

      &:hover {
        border-color: var(--accent);
      }

      &:focus-visible {
        outline: 2px solid var(--accent);
        outline-offset: 2px;
      }
    }

    .metric-visualization, .metric-table {
      background: var(--bg-surface);
      border: 1px solid var(--border);
      border-radius: var(--radius-md);
      padding: 1.5rem;
    }

    .table-heading {
      font-size: 14px;
      font-weight: 600;
      color: var(--text-secondary);
      margin: 0 0 1rem;
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    .empty-state, .list-loading, .data-loading {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 4rem 2rem;
      color: var(--text-muted);
      font-style: italic;
      gap: 1rem;
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
export class MetricsExplorerComponent implements OnInit {
  private service = inject(MetricsExplorerService);

  metricNames = signal<string[]>([]);
  selectedMetric = signal<string | null>(null);
  chartData = signal<ChartDataPoint[]>([]);
  tableData = signal<TableRow[]>([]);
  lookback = '1h';

  isLoadingNames = signal(false);
  isLoadingData = signal(false);

  tableColumns: TableColumn[] = [
    { key: 'timestamp', label: 'Timestamp', sortable: true },
    { key: 'value', label: 'Value', sortable: true }
  ];

  ngOnInit(): void {
    this.loadMetricNames();
  }

  private loadMetricNames(): void {
    this.isLoadingNames.set(true);
    this.service.getMetricNames().subscribe({
      next: (names) => {
        this.metricNames.set(names);
        this.isLoadingNames.set(false);
      },
      error: (err) => {
        console.error('Failed to load metric names:', err);
        this.isLoadingNames.set(false);
      }
    });
  }

  selectMetric(name: string): void {
    this.selectedMetric.set(name);
    this.loadMetricData();
  }

  loadMetricData(): void {
    const metric = this.selectedMetric();
    if (!metric) return;

    this.isLoadingData.set(true);
    this.service.getMetricData(metric, this.lookback).subscribe({
      next: (response) => {
        // Transform query response to chart/table data
        const data: ChartDataPoint[] = response.rows.map((row) => ({
          timestamp: this.extractTimestamp(row.values[0]),
          value: this.extractNumber(row.values[1])
        }));

        this.chartData.set(data);
        this.tableData.set(
          data.map(d => ({
            timestamp: new Date(d.timestamp!).toLocaleString(),
            value: d.value.toFixed(2)
          }))
        );

        this.isLoadingData.set(false);
      },
      error: (err) => {
        console.error('Failed to load metric data:', err);
        this.chartData.set([]);
        this.tableData.set([]);
        this.isLoadingData.set(false);
      }
    });
  }

  private extractTimestamp(value: any): string {
    if ('timestamp' in value) return value.timestamp;
    if ('text' in value) return value.text;
    return new Date().toISOString();
  }

  private extractNumber(value: any): number {
    if ('number' in value) return value.number;
    if ('text' in value) return parseFloat(value.text) || 0;
    return 0;
  }
}
