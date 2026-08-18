import { Component, inject, signal, OnInit, DestroyRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { interval } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ExportersService, ExporterStatus } from '../../core/services/exporters.service';
import { StatusBadgeComponent } from '../../shared/status-badge/status-badge.component';

@Component({
  selector: 'app-exporters',
  standalone: true,
  imports: [CommonModule, StatusBadgeComponent],
  template: `
    <div class="exporters-page">
      <header class="page-header">
        <h1 class="page-title">Exporters</h1>
        <p class="page-subtitle">Monitor the health and performance of metric exporters</p>
      </header>

      @if (isLoading()) {
        <div class="loading-state" role="status" aria-live="polite">
          <span class="loading-spinner" aria-hidden="true"></span>
          <span>Loading exporters...</span>
        </div>
      } @else if (error()) {
        <div class="error-panel" role="alert" aria-live="assertive">
          <span class="error-icon" aria-hidden="true">⚠</span>
          <div class="error-content">
            <strong>Failed to Load Exporters</strong>
            <p>{{ error() }}</p>
          </div>
          <button class="retry-button" (click)="loadStatus()" type="button">
            Retry
          </button>
        </div>
      } @else if (exporters().length === 0) {
        <div class="empty-state surface">
          <span class="empty-icon" aria-hidden="true">📤</span>
          <p>No exporters configured</p>
        </div>
      } @else {
        <!-- Summary Cards -->
        <section class="summary-section" role="region" aria-label="Exporter summary">
          <div class="summary-grid">
            <article class="summary-card surface">
              <div class="summary-label">Total Exporters</div>
              <div class="summary-value">{{ exporters().length }}</div>
            </article>

            <article class="summary-card surface">
              <div class="summary-label">Healthy</div>
              <div class="summary-value success">{{ healthyCount() }}</div>
            </article>

            <article class="summary-card surface">
              <div class="summary-label">Unhealthy</div>
              <div class="summary-value danger">{{ unhealthyCount() }}</div>
            </article>

            <article class="summary-card surface">
              <div class="summary-label">Total Exports</div>
              <div class="summary-value">{{ totalExports() }}</div>
            </article>
          </div>
        </section>

        <!-- Exporter Cards -->
        <section class="exporters-section" role="region" aria-label="Exporter details">
          <div class="exporters-grid" role="list">
            @for (exporter of exporters(); track exporter.name) {
              <article class="exporter-card surface" role="listitem">
                <div class="card-header">
                  <div class="exporter-info">
                    <h2 class="exporter-name">{{ exporter.name }}</h2>
                    <app-status-badge
                      [status]="exporter.isHealthy ? 'healthy' : 'unhealthy'"
                      [label]="exporter.isHealthy ? 'Healthy' : 'Unhealthy'"
                    />
                  </div>
                </div>

                <dl class="exporter-stats">
                  <div class="stat-row">
                    <dt>Total Exports</dt>
                    <dd class="stat-value">{{ exporter.totalExports.toLocaleString() }}</dd>
                  </div>

                  <div class="stat-row">
                    <dt>Total Failures</dt>
                    <dd class="stat-value" [class.has-failures]="exporter.totalFailures > 0">
                      {{ exporter.totalFailures.toLocaleString() }}
                    </dd>
                  </div>

                  <div class="stat-row">
                    <dt>Success Rate</dt>
                    <dd class="stat-value">
                      {{ getSuccessRate(exporter) }}%
                    </dd>
                  </div>

                  <div class="stat-row">
                    <dt>Last Success</dt>
                    <dd class="stat-value">
                      {{ formatTimestamp(exporter.lastSuccessUtc) }}
                    </dd>
                  </div>

                  @if (exporter.lastFailureUtc) {
                    <div class="stat-row">
                      <dt>Last Failure</dt>
                      <dd class="stat-value failure-time">
                        {{ formatTimestamp(exporter.lastFailureUtc) }}
                      </dd>
                    </div>
                  }
                </dl>

                @if (exporter.lastError) {
                  <div class="error-details" role="alert">
                    <div class="error-label">Last Error</div>
                    <div class="error-message">{{ exporter.lastError }}</div>
                  </div>
                }

                <!-- Health Indicator Bar -->
                <div class="health-bar" [attr.aria-label]="'Health: ' + getSuccessRate(exporter) + '%'">
                  <div
                    class="health-fill"
                    [class.healthy]="exporter.isHealthy"
                    [class.unhealthy]="!exporter.isHealthy"
                    [style.width.%]="getSuccessRate(exporter)"
                    role="progressbar"
                    [attr.aria-valuenow]="getSuccessRate(exporter)"
                    aria-valuemin="0"
                    aria-valuemax="100">
                  </div>
                </div>
              </article>
            }
          </div>
        </section>

        <!-- Auto-refresh indicator -->
        <div class="refresh-indicator" role="status" aria-live="polite">
          <span class="refresh-dot" aria-hidden="true"></span>
          <span>Auto-refreshing every 30 seconds</span>
        </div>
      }
    </div>
  `,
  styles: [`
    .exporters-page {
      display: flex;
      flex-direction: column;
      gap: 1.5rem;
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

    .loading-state, .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 1rem;
      padding: 4rem 2rem;
      color: var(--text-muted);
    }

    .empty-state {
      font-style: italic;
    }

    .empty-icon {
      font-size: 48px;
      opacity: 0.5;
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

    .error-panel {
      display: flex;
      align-items: center;
      gap: 1rem;
      padding: 1rem 1.25rem;
      background: rgba(239, 68, 68, 0.1);
      border: 1px solid rgba(239, 68, 68, 0.3);
      border-radius: var(--radius-md);
      color: var(--danger);
    }

    .error-icon {
      font-size: 20px;
      flex-shrink: 0;
    }

    .error-content {
      flex: 1;

      strong {
        display: block;
        font-weight: 600;
        margin-bottom: 0.25rem;
      }

      p {
        margin: 0;
        font-size: 13px;
      }
    }

    .retry-button {
      padding: 0.5rem 1rem;
      background: var(--danger);
      color: white;
      border: none;
      border-radius: var(--radius-sm);
      font-weight: 600;
      font-size: 13px;
      cursor: pointer;
      transition: all var(--transition);

      &:hover {
        background: var(--accent);
      }

      &:focus-visible {
        outline: 2px solid var(--accent);
        outline-offset: 2px;
      }
    }

    .summary-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
      gap: 1rem;
    }

    .summary-card {
      padding: 1.25rem;
      text-align: center;
    }

    .summary-label {
      font-size: 12px;
      font-weight: 600;
      color: var(--text-secondary);
      text-transform: uppercase;
      letter-spacing: 0.05em;
      margin-bottom: 0.5rem;
    }

    .summary-value {
      font-size: 32px;
      font-weight: 700;
      color: var(--text-primary);

      &.success {
        color: var(--success);
      }

      &.danger {
        color: var(--danger);
      }
    }

    .exporters-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(400px, 1fr));
      gap: 1.5rem;

      @media (max-width: 768px) {
        grid-template-columns: 1fr;
      }
    }

    .exporter-card {
      padding: 1.5rem;
      display: flex;
      flex-direction: column;
      gap: 1rem;
      transition: all var(--transition);

      &:hover {
        border-color: rgba(20, 184, 166, 0.3);
        transform: translateY(-2px);
        box-shadow: var(--shadow-md);
      }
    }

    .card-header {
      padding-bottom: 1rem;
      border-bottom: 1px solid var(--border);
    }

    .exporter-info {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 1rem;
    }

    .exporter-name {
      font-size: 20px;
      font-weight: 600;
      color: var(--text-primary);
      margin: 0;
    }

    .exporter-stats {
      margin: 0;
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 0.75rem;
    }

    .stat-row {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;

      dt {
        font-size: 11px;
        font-weight: 600;
        color: var(--text-muted);
        text-transform: uppercase;
        letter-spacing: 0.05em;
      }

      dd {
        margin: 0;
      }
    }

    .stat-value {
      font-size: 18px;
      font-weight: 600;
      color: var(--text-primary);

      &.has-failures {
        color: var(--danger);
      }

      &.failure-time {
        color: var(--warning);
      }
    }

    .error-details {
      padding: 0.75rem;
      background: rgba(239, 68, 68, 0.1);
      border: 1px solid rgba(239, 68, 68, 0.3);
      border-radius: var(--radius-sm);
    }

    .error-label {
      font-size: 11px;
      font-weight: 600;
      color: var(--danger);
      text-transform: uppercase;
      letter-spacing: 0.05em;
      margin-bottom: 0.5rem;
    }

    .error-message {
      font-size: 13px;
      color: var(--text-primary);
      font-family: 'Monaco', 'Courier New', monospace;
      word-break: break-word;
    }

    .health-bar {
      height: 6px;
      background: var(--bg-elevated);
      border-radius: 3px;
      overflow: hidden;
    }

    .health-fill {
      height: 100%;
      transition: width var(--transition-slow);

      &.healthy {
        background: linear-gradient(90deg, var(--success), var(--accent));
      }

      &.unhealthy {
        background: linear-gradient(90deg, var(--danger), var(--warning));
      }
    }

    .refresh-indicator {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 0.5rem;
      padding: 0.75rem;
      color: var(--text-muted);
      font-size: 12px;
    }

    .refresh-dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: var(--accent);
      animation: pulse 2s infinite;
    }

    @keyframes pulse {
      0%, 100% {
        opacity: 1;
      }
      50% {
        opacity: 0.4;
      }
    }
  `]
})
export class ExportersComponent implements OnInit {
  private exportersService = inject(ExportersService);
  private destroyRef = inject(DestroyRef);

  exporters = signal<ExporterStatus[]>([]);
  isLoading = signal(false);
  error = signal<string | null>(null);

  healthyCount = signal(0);
  unhealthyCount = signal(0);
  totalExports = signal(0);

  ngOnInit(): void {
    this.loadStatus();

    // Auto-refresh every 30 seconds
    interval(30000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.loadStatus());
  }

  loadStatus(): void {
    this.isLoading.set(true);
    this.error.set(null);

    this.exportersService.getStatus().subscribe({
      next: (status) => {
        this.exporters.set(status);
        this.updateSummary(status);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.error.set(err.error?.message || err.message || 'Failed to load exporters');
        this.isLoading.set(false);
      }
    });
  }

  getSuccessRate(exporter: ExporterStatus): number {
    if (exporter.totalExports === 0) return 100;
    const successCount = exporter.totalExports - exporter.totalFailures;
    return Math.round((successCount / exporter.totalExports) * 100);
  }

  formatTimestamp(timestamp: string | null): string {
    if (!timestamp) return 'Never';
    const date = new Date(timestamp);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffMins = Math.floor(diffMs / 60000);

    if (diffMins < 1) return 'Just now';
    if (diffMins < 60) return `${diffMins}m ago`;
    if (diffMins < 1440) return `${Math.floor(diffMins / 60)}h ago`;
    return date.toLocaleDateString();
  }

  private updateSummary(status: ExporterStatus[]): void {
    this.healthyCount.set(status.filter(e => e.isHealthy).length);
    this.unhealthyCount.set(status.filter(e => !e.isHealthy).length);
    this.totalExports.set(status.reduce((sum, e) => sum + e.totalExports, 0));
  }
}
