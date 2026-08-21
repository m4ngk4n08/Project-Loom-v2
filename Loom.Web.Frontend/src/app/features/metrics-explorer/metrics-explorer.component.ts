import { Component, inject, signal, computed, OnInit, OnDestroy, ViewChild, ElementRef, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MetricsExplorerService, MetricSummary } from '../../core/services/metrics-explorer.service';
import { ExplorerPersistenceService } from '../../core/services/explorer-persistence.service';
import { MetricChartComponent, ChartDataPoint } from '../../shared/metric-chart/metric-chart.component';
import { ComparisonChartComponent, ComparisonSeries } from '../../shared/comparison-chart/comparison-chart.component';
import { DataTableComponent, TableColumn, TableRow } from '../../shared/data-table/data-table.component';

@Component({
  selector: 'app-metrics-explorer',
  standalone: true,
  imports: [CommonModule, FormsModule, MetricChartComponent, ComparisonChartComponent, DataTableComponent],
  template: `
    <div class="metrics-explorer-page">
      <header class="page-header">
        <h1 class="page-title">Metrics Explorer</h1>
        <p class="page-subtitle">Browse and analyze all available metrics</p>
      </header>

      <div class="explorer-layout" [class.collapsed]="isListCollapsed()">
        <!-- Metrics List (Left Panel) -->
        <aside class="metrics-list surface" role="complementary" aria-label="Available metrics">
          <div class="list-header">
            <h2 class="list-title">Available Metrics</h2>
            <div class="list-header-actions">
              <span class="metrics-count" [attr.aria-label]="filteredSummaries().length + ' metrics shown'">
                {{ filteredSummaries().length }}/{{ summaries().length }}
              </span>
              <button
                class="collapse-btn"
                (click)="isListCollapsed.set(true)"
                [attr.aria-label]="'Collapse metrics list'"
                title="Collapse metrics list"
                type="button">
                &#8249;
              </button>
            </div>
          </div>

          @if (!isListCollapsed()) {
          <div class="list-filters" role="group" aria-label="Filter metrics">
            <input
              class="filter-input"
              type="search"
              [(ngModel)]="searchText"
              placeholder="Search metrics..."
              [attr.aria-label]="'Search metrics'"
              spellcheck="false" />
            <select
              class="type-filter"
              [(ngModel)]="typeFilter"
              [attr.aria-label]="'Filter by metric type'">
              <option value="">All types</option>
              <option value="counter">Counter</option>
              <option value="gauge">Gauge</option>
              <option value="histogram">Histogram</option>
              <option value="method">Method</option>
            </select>
          </div>

          @if (loadError()) {
            <div class="list-error" role="alert">
              <p>{{ loadError() }}</p>
              <button
                class="retry-btn"
                (click)="loadMetricNames()"
                [attr.aria-label]="'Retry loading metrics'"
                type="button">
                Retry
              </button>
            </div>
          } @else if (isLoadingNames() && summaries().length === 0) {
            <div class="list-skeleton" role="status" aria-live="polite" aria-label="Loading metrics">
              @for (i of [1, 2, 3, 4, 5]; track i) {
                <div class="skeleton-row">
                  <span class="skeleton-block skeleton-name"></span>
                  <span class="skeleton-block skeleton-meta"></span>
                </div>
              }
            </div>
          } @else if (summaries().length === 0) {
            <div class="list-empty">
              <p>No metrics available</p>
            </div>
          } @else if (filteredSummaries().length === 0) {
            <div class="list-empty">
              <p>No metrics match your filters</p>
            </div>
          } @else {
            <div class="list-sortbar" role="group" aria-label="Sort metrics">
              <button
                class="sort-btn"
                [class.active]="sortField() === 'name'"
                (click)="toggleSort('name')"
                [attr.aria-label]="'Sort by name'"
                [attr.aria-sort]="sortAriaLabel('name')"
                type="button">
                Name @if (sortField() === 'name') { {{ sortDir() === 'asc' ? '\u2191' : '\u2193' }} }
              </button>
              <button
                class="sort-btn"
                [class.active]="sortField() === 'latestValue'"
                (click)="toggleSort('latestValue')"
                [attr.aria-label]="'Sort by latest value'"
                [attr.aria-sort]="sortAriaLabel('latestValue')"
                type="button">
                Value @if (sortField() === 'latestValue') { {{ sortDir() === 'asc' ? '\u2191' : '\u2193' }} }
              </button>
              <button
                class="sort-btn"
                [class.active]="sortField() === 'sampleCount'"
                (click)="toggleSort('sampleCount')"
                [attr.aria-label]="'Sort by sample count'"
                [attr.aria-sort]="sortAriaLabel('sampleCount')"
                type="button">
                Samples @if (sortField() === 'sampleCount') { {{ sortDir() === 'asc' ? '\u2191' : '\u2193' }} }
              </button>
            </div>
            <ul class="metric-items" role="list" #metricList>
              @for (summary of filteredSummaries(); track summary.name) {
                <li class="metric-item">
                  <button
                    class="metric-button"
                    [class.active]="selectedMetric() === summary.name"
                    (click)="selectMetric(summary.name)"
                    [attr.aria-label]="'Select ' + summary.name + ' metric'"
                    [attr.aria-pressed]="selectedMetric() === summary.name"
                    type="button">
                    <span class="metric-name">{{ summary.name }}</span>
                    <span class="metric-meta">
                      <span class="type-badge" [class]="'type-' + summary.type">{{ summary.type }}</span>
                      <span class="metric-samples">{{ summary.sampleCount }} samples</span>
                    </span>
                    <span class="metric-latest">
                      {{ formatValue(summary.latestValue, summary.unit) }} <span class="metric-unit">{{ summary.unit }}</span>
                    </span>
                  </button>
                  <button
                    class="compare-btn"
                    [class.active]="isComparing(summary.name)"
                    (click)="toggleCompare(summary.name)"
                    [attr.aria-label]="isComparing(summary.name) ? 'Remove ' + summary.name + ' from comparison' : 'Add ' + summary.name + ' to comparison'"
                    [attr.aria-pressed]="isComparing(summary.name)"
                    title="Compare"
                    type="button">
                    &#x21c4;
                  </button>
                </li>
              }
            </ul>
          }
          }
        </aside>

        <!-- Metric Details (Right Panel) -->
        <main class="metric-details" role="main">
          @if (isListCollapsed()) {
            <button
              class="expand-btn"
              (click)="isListCollapsed.set(false)"
              [attr.aria-label]="'Show metrics list'"
              title="Show metrics list"
              type="button">
              &#8250; Available Metrics
            </button>
          }

          @if (compareMetrics().length > 0) {
            <div class="compare-panel" role="region" aria-label="Metric comparison">
              <div class="compare-panel-header">
                <h3 class="compare-title">Comparison</h3>
                <span class="compare-count">{{ compareMetrics().length }} metrics</span>
                <button
                  class="compare-clear"
                  (click)="clearComparison()"
                  [attr.aria-label]="'Clear comparison'"
                  title="Clear comparison"
                  type="button">
                  Clear
                </button>
              </div>

              <div class="compare-chips" role="list" aria-label="Compared metrics">
                @for (name of compareMetrics(); track name) {
                  <span class="compare-chip" role="listitem">
                    <span class="chip-color" [style.background]="seriesColor(name)" aria-hidden="true"></span>
                    <span class="chip-name">{{ name }}</span>
                    <button
                      class="chip-remove"
                      (click)="toggleCompare(name)"
                      [attr.aria-label]="'Remove ' + name + ' from comparison'"
                      title="Remove from comparison"
                      type="button">
                      ×
                    </button>
                  </span>
                }
              </div>

              @if (compareError()) {
                <div class="compare-error" role="alert">
                  <p>{{ compareError() }}</p>
                  <button
                    class="retry-btn"
                    (click)="loadComparisonData()"
                    [attr.aria-label]="'Retry loading comparison data'"
                    type="button">
                    Retry
                  </button>
                </div>
              } @else if (compareLoading() && comparisonSeries().length === 0) {
                <div class="compare-loading" role="status">
                  <span class="loading-spinner" aria-hidden="true"></span>
                  <span>Loading comparison data...</span>
                </div>
              } @else if (comparisonSeries().length > 0) {
                <div class="compare-chart">
                  <app-comparison-chart
                    title="Metric Comparison"
                    [series]="comparisonSeries()"
                    height="320px"
                    ariaLabel="Overlaid comparison of selected metrics over time"
                  />
                </div>
              } @else {
                <p class="compare-empty">No data available for the selected metrics in this time range</p>
              }
            </div>
          }

          @if (!selectedMetric()) {
            <div class="empty-state">
              <p>Select a metric from the list to view its data</p>
            </div>
          } @else {
            <div class="details-content">
              @if (drilldownAt != null) {
                <div class="drilldown-banner" role="status">
                  <span class="drilldown-icon" aria-hidden="true">⌖</span>
                  <span class="drilldown-text">
                    Drilled down from dashboard
                    @if (drilldownMetric) { <strong>{{ drilldownMetric }}</strong> }
                    @ {{ (drilldownAt | date: 'medium') || '' }}
                  </span>
                  <button
                    class="drilldown-clear"
                    (click)="clearDrilldown()"
                    [attr.aria-label]="'Clear drill-down'"
                    title="Clear drill-down"
                    type="button">
                    ×
                  </button>
                </div>
              }
              <div class="details-header">
                <div class="title-group">
                  <h2 class="metric-title">{{ selectedMetric() }}</h2>
                  @if (selectedSummary()) {
                    <div class="title-badges">
                      <span class="type-badge" [class]="'type-' + selectedSummary()!.type">{{ selectedSummary()!.type }}</span>
                      <span class="unit-badge">{{ selectedSummary()!.unit }}</span>
                    </div>
                  }
                </div>
                <div class="lookback-selector" role="group" aria-label="Time range selection">
                  <label for="lookback-select" class="sr-only">Select time range</label>
                  <select
                    id="lookback-select"
                    class="lookback-select"
                    [ngModel]="lookback()"
                    (ngModelChange)="onLookbackChange($event)"
                    [attr.aria-label]="'Time range: ' + lookback()">
                    <option value="5m">Last 5 minutes</option>
                    <option value="15m">Last 15 minutes</option>
                    <option value="1h">Last hour</option>
                    <option value="6h">Last 6 hours</option>
                    <option value="24h">Last 24 hours</option>
                  </select>
                </div>
              </div>

              @if (dataError()) {
                <div class="data-error" role="alert">
                  <p>{{ dataError() }}</p>
                  <button
                    class="retry-btn"
                    (click)="loadMetricData()"
                    [attr.aria-label]="'Retry loading metric data'"
                    type="button">
                    Retry
                  </button>
                </div>
              } @else if (isLoadingData() && chartData().length === 0) {
                <div class="data-loading" role="status" aria-live="polite">
                  <span class="loading-spinner" aria-hidden="true"></span>
                  <span>Loading metric data...</span>
                </div>
              } @else if (chartData().length > 0) {
                <div class="stat-tiles" role="group" aria-label="Metric statistics">
                  <div class="stat-tile">
                    <span class="stat-label">Samples</span>
                    <span class="stat-value">{{ statCount() }}</span>
                  </div>
                  <div class="stat-tile">
                    <span class="stat-label">Latest</span>
                    <span class="stat-value">{{ formatValue(statLatest(), selectedSummary()?.unit) }}</span>
                  </div>
                  <div class="stat-tile">
                    <span class="stat-label">Average</span>
                    <span class="stat-value">{{ formatValue(statAverage(), selectedSummary()?.unit) }}</span>
                  </div>
                  <div class="stat-tile">
                    <span class="stat-label">Min</span>
                    <span class="stat-value">{{ formatValue(statMin(), selectedSummary()?.unit) }}</span>
                  </div>
                  <div class="stat-tile">
                    <span class="stat-label">Max</span>
                    <span class="stat-value">{{ formatValue(statMax(), selectedSummary()?.unit) }}</span>
                  </div>
                  <div class="stat-tile">
                    <span class="stat-label">P95</span>
                    <span class="stat-value">{{ formatValue(statP95(), selectedSummary()?.unit) }}</span>
                  </div>
                </div>

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
                    [highlightPredicate]="drilldownHighlight()"
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
  styleUrls: ['./metrics-explorer.component.scss']
})
export class MetricsExplorerComponent implements OnInit, OnDestroy {
  private service = inject(MetricsExplorerService);
  private persistence = inject(ExplorerPersistenceService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private refreshTimer: ReturnType<typeof setInterval> | null = null;
  private readonly REFRESH_MS = 2500;

  drilldownAt: number | null = null;
  drilldownMetric: string | null = null;

  @ViewChild('metricList') private metricList!: ElementRef<HTMLElement>;
  private lastScrolledMetric: string | null = null;

  constructor() {
    effect(() => {
      const selected = this.selectedMetric();
      const list = this.summaries();
      if (selected && list.length > 0 && selected !== this.lastScrolledMetric) {
        this.lastScrolledMetric = selected;
        requestAnimationFrame(() => this.scrollToSelectedMetric());
      }
    });

    effect(() => {
      this.persistViewState();
    });
  }

  private scrollToSelectedMetric(): void {
    const container = this.metricList?.nativeElement;
    if (!container) return;
    const active = container.querySelector('.active');
    if (active) active.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  }

  summaries = signal<MetricSummary[]>([]);
  selectedMetric = signal<string | null>(null);
  chartData = signal<ChartDataPoint[]>([]);
  tableData = signal<TableRow[]>([]);
  lookback = signal('1h');

  compareMetrics = signal<string[]>([]);
  comparisonSeries = signal<ComparisonSeries[]>([]);
  compareLoading = signal(false);
  compareError = signal<string | null>(null);

  private readonly SERIES_COLORS = [
    '#14b8a6', // Teal
    '#3b82f6', // Blue
    '#8b5cf6', // Purple
    '#f59e0b', // Amber
    '#ec4899', // Pink
    '#22c55e'  // Green
  ];

  searchText = signal('');
  typeFilter = signal('');
  sortField = signal<'name' | 'sampleCount' | 'latestValue'>('name');
  sortDir = signal<'asc' | 'desc'>('asc');

  isLoadingNames = signal(false);
  isListCollapsed = signal(false);
  isLoadingData = signal(false);
  loadError = signal<string | null>(null);
  dataError = signal<string | null>(null);

  statCount = signal(0);
  statLatest = signal(0);
  statAverage = signal(0);
  statMin = signal(0);
  statMax = signal(0);
  statP95 = signal(0);

  tableColumns: TableColumn[] = [
    { key: 'method', label: 'Metric', sortable: true },
    { key: 'value', label: 'Value', sortable: true },
    { key: 'timestamp', label: 'Timestamp', sortable: true },
    { key: 'type', label: 'Type', sortable: true }
  ];

  filteredSummaries = computed(() => {
    const query = this.searchText().trim().toLowerCase();
    const type = this.typeFilter().toLowerCase();
    const dir = this.sortDir() === 'asc' ? 1 : -1;
    const field = this.sortField();

    return this.summaries()
      .filter(s =>
        (query === '' || s.name.toLowerCase().includes(query)) &&
        (type === '' || s.type.toLowerCase() === type)
      )
      .sort((a, b) => {
        const cmp = field === 'name'
          ? a.name.localeCompare(b.name)
          : field === 'sampleCount'
            ? a.sampleCount - b.sampleCount
            : a.latestValue - b.latestValue;
        return cmp * dir;
      });
  });

  selectedSummary = computed(() =>
    this.summaries().find(s => s.name === this.selectedMetric()) ?? null
  );

  drilldownHighlightLabel = computed<string | null>(() => {
    const at = this.drilldownAt;
    if (at == null || this.tableData().length === 0) return null;
    let best: string | null = null;
    let bestDiff = Infinity;
    for (const row of this.tableData()) {
      const t = new Date(row['timestamp']).getTime();
      const diff = Math.abs(t - at);
      if (diff < bestDiff) {
        bestDiff = diff;
        best = row['timestamp'];
      }
    }
    return bestDiff <= 10_000 ? best : null;
  });

  drilldownHighlight = computed<((row: TableRow) => boolean) | undefined>(() => {
    const label = this.drilldownHighlightLabel();
    if (label == null) return undefined;
    return (row: TableRow) => row['timestamp'] === label;
  });

  clearDrilldown(): void {
    this.router.navigate(['/metrics'], { queryParams: {} });
  }

  ngOnInit(): void {
    this.restoreViewState();
    this.route.queryParamMap.subscribe((params) => {
      this.drilldownAt = params.get('at') ? new Date(params.get('at')!).getTime() : null;
      this.drilldownMetric = params.get('metric') ?? null;
      if (this.drilldownMetric) {
        this.selectMetric(this.drilldownMetric);
      }
    });
    this.loadMetricNames();
    this.refreshTimer = setInterval(() => this.refresh(), this.REFRESH_MS);
  }

  ngOnDestroy(): void {
    if (this.refreshTimer) {
      clearInterval(this.refreshTimer);
      this.refreshTimer = null;
    }
  }

  private restoreViewState(): void {
    const saved = this.persistence.state();
    this.searchText.set(saved.searchText);
    this.typeFilter.set(saved.typeFilter);
    this.sortField.set(saved.sortField);
    this.sortDir.set(saved.sortDir);
    this.isListCollapsed.set(saved.isListCollapsed);
    this.lookback.set(saved.lookback);
    this.compareMetrics.set(saved.compareMetrics);
    if (saved.selectedMetric) {
      this.selectMetric(saved.selectedMetric);
    }
    if (saved.compareMetrics.length > 0) {
      this.loadComparisonData();
    }
  }

  private persistViewState(): void {
    this.persistence.save({
      selectedMetric: this.selectedMetric(),
      searchText: this.searchText(),
      typeFilter: this.typeFilter(),
      sortField: this.sortField(),
      sortDir: this.sortDir(),
      isListCollapsed: this.isListCollapsed(),
      lookback: this.lookback(),
      compareMetrics: this.compareMetrics()
    });
  }

  isComparing(name: string): boolean {
    return this.compareMetrics().includes(name);
  }

  toggleCompare(name: string): void {
    const current = this.compareMetrics();
    this.compareMetrics.set(
      current.includes(name)
        ? current.filter(m => m !== name)
        : [...current, name]
    );
    this.loadComparisonData();
  }

  clearComparison(): void {
    this.compareMetrics.set([]);
    this.comparisonSeries.set([]);
    this.compareError.set(null);
  }

  seriesColor(name: string): string {
    const idx = this.compareMetrics().indexOf(name);
    return this.SERIES_COLORS[idx % this.SERIES_COLORS.length] ?? this.SERIES_COLORS[0];
  }

  loadComparisonData(): void {
    const names = this.compareMetrics();
    if (names.length === 0) {
      this.comparisonSeries.set([]);
      this.compareError.set(null);
      return;
    }

    this.compareLoading.set(true);
    this.compareError.set(null);

    const lookback = this.lookback();
    const requests = names.map(name => this.service.getMetricData(name, lookback));

    // Sequential fetching avoids hammering the backend with N parallel requests.
    this.fetchComparisonSeries(names, requests, 0, []);
  }

  private fetchComparisonSeries(
    names: string[],
    requests: ReturnType<MetricsExplorerService['getMetricData']>[],
    index: number,
    acc: ComparisonSeries[]
  ): void {
    if (index >= names.length) {
      this.comparisonSeries.set(acc);
      this.compareLoading.set(false);
      return;
    }

    requests[index].subscribe({
      next: (response) => {
        const valIdx = response.columns.indexOf('value');
        const tsIdx = response.columns.indexOf('timestamp');
        const data: ChartDataPoint[] = [];
        for (const row of response.rows) {
          const ts = this.extractTimestamp(tsIdx >= 0 ? row.values[tsIdx] : undefined);
          const value = this.extractNumber(valIdx >= 0 ? row.values[valIdx] : undefined);
          if (Number.isNaN(value)) continue;
          data.push({ timestamp: ts, value });
        }
        acc.push({ name: names[index], data, color: this.seriesColor(names[index]) });
        this.fetchComparisonSeries(names, requests, index + 1, acc);
      },
      error: (err) => {
        console.error(`Failed to load comparison data for ${names[index]}:`, err);
        this.compareLoading.set(false);
        this.compareError.set('Could not load comparison data for one or more metrics.');
      }
    });
  }

  onLookbackChange(value: string): void {
    this.lookback.set(value);
    this.loadMetricData();
    if (this.compareMetrics().length > 0) {
      this.loadComparisonData();
    }
  }

  private refresh(): void {
    this.loadMetricNames(true);
    if (this.selectedMetric()) {
      this.loadMetricData(true);
    }
    if (this.compareMetrics().length > 0) {
      this.loadComparisonData();
    }
  }

  toggleSort(field: 'name' | 'sampleCount' | 'latestValue'): void {
    if (this.sortField() === field) {
      this.sortDir.set(this.sortDir() === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortField.set(field);
      this.sortDir.set('asc');
    }
  }

  sortAriaLabel(field: 'name' | 'sampleCount' | 'latestValue'): string | null {
    if (this.sortField() !== field) return null;
    return this.sortDir() === 'asc' ? 'ascending' : 'descending';
  }

  loadMetricNames(silent = false): void {
    if (!silent) this.isLoadingNames.set(true);
    this.loadError.set(null);
    this.service.getMetricSummary().subscribe({
      next: (summaries) => {
        this.summaries.set(summaries);
        this.isLoadingNames.set(false);
      },
      error: (err) => {
        console.error('Failed to load metric summary:', err);
        this.isLoadingNames.set(false);
        if (!silent) {
          this.loadError.set('Could not load metrics. Is a target process running?');
        }
      }
    });
  }

  selectMetric(name: string): void {
    this.selectedMetric.set(name);
    this.loadMetricData();
  }

  loadMetricData(silent = false): void {
    const metric = this.selectedMetric();
    if (!metric) return;

    if (!silent) this.isLoadingData.set(true);
    this.dataError.set(null);
    this.service.getMetricData(metric, this.lookback()).subscribe({
      next: (response) => {
        // Find column indices by name (raw mode returns method, value, timestamp, type)
        const methodIdx = response.columns.indexOf('method');
        const tsIdx = response.columns.indexOf('timestamp');
        const valIdx = response.columns.indexOf('value');
        const typeIdx = response.columns.indexOf('type');

        const data: ChartDataPoint[] = [];
        const rows: TableRow[] = [];

        for (const row of response.rows) {
          const ts = this.extractTimestamp(tsIdx >= 0 ? row.values[tsIdx] : undefined);
          const value = this.extractNumber(valIdx >= 0 ? row.values[valIdx] : undefined);
          if (Number.isNaN(value)) continue;

          data.push({ timestamp: ts, value });

          rows.push({
            method: methodIdx >= 0 ? this.extractText(row.values[methodIdx]) : metric,
            value: this.formatValue(value, this.selectedSummary()?.unit),
            timestamp: new Date(ts).toLocaleString(),
            type: typeIdx >= 0 ? this.extractText(row.values[typeIdx]) : '-'
          });
        }

        this.chartData.set(data);
        this.tableData.set(rows);
        this.computeStats(data);
        this.isLoadingData.set(false);
      },
      error: (err) => {
        console.error('Failed to load metric data:', err);
        this.chartData.set([]);
        this.tableData.set([]);
        this.isLoadingData.set(false);
        if (!silent) {
          this.dataError.set('Could not load data for this metric.');
        }
      }
    });
  }

  private computeStats(data: ChartDataPoint[]): void {
    if (data.length === 0) {
      this.statCount.set(0);
      this.statLatest.set(0);
      this.statAverage.set(0);
      this.statMin.set(0);
      this.statMax.set(0);
      this.statP95.set(0);
      return;
    }

    const values = data.map(d => d.value).sort((a, b) => a - b);
    this.statCount.set(values.length);
    this.statLatest.set(data[0].value);
    this.statAverage.set(values.reduce((a, b) => a + b, 0) / values.length);
    this.statMin.set(values[0]);
    this.statMax.set(values[values.length - 1]);
    this.statP95.set(values[Math.min(values.length - 1, Math.ceil(0.95 * values.length) - 1)]);
  }

  formatValue(value: number, unit?: string): string {
    if (!Number.isFinite(value)) return '-';
    const formatted = Math.abs(value) >= 1000
      ? value.toLocaleString(undefined, { maximumFractionDigits: 0 })
      : value.toFixed(2);
    return unit ? `${formatted} ${unit}` : formatted;
  }

  private extractText(value: any): string {
    if (value && 'text' in value) return value.text;
    return String(value ?? '-');
  }

  private extractTimestamp(value: any): string {
    if (value && 'timestamp' in value) return value.timestamp;
    return new Date().toISOString();
  }

  private extractNumber(value: any): number {
    if (value && 'number' in value) return value.number;
    if (value && 'text' in value) return parseFloat(value.text);
    return NaN;
  }
}
