import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AlertsService, AlertRule } from '../../core/services/alerts.service';
import { StatusBadgeComponent } from '../../shared/status-badge/status-badge.component';

@Component({
  selector: 'app-alerts',
  standalone: true,
  imports: [CommonModule, StatusBadgeComponent],
  template: `
    <div class="alerts-page">
      <header class="page-header">
        <h1 class="page-title">Alerts</h1>
        <p class="page-subtitle">Monitor and manage alert rules and their firing history</p>
      </header>

      @if (isLoading()) {
        <div class="loading-state" role="status" aria-live="polite">
          <span class="loading-spinner" aria-hidden="true"></span>
          <span>Loading alerts...</span>
        </div>
      } @else if (error()) {
        <div class="error-panel" role="alert" aria-live="assertive">
          <span class="error-icon" aria-hidden="true">⚠</span>
          <div class="error-content">
            <strong>Failed to Load Alerts</strong>
            <p>{{ error() }}</p>
          </div>
          <button class="retry-button" (click)="loadAlerts()" type="button">
            Retry
          </button>
        </div>
      } @else if (alerts().length === 0) {
        <div class="empty-state surface">
          <span class="empty-icon" aria-hidden="true">🔔</span>
          <p>No alert rules configured</p>
        </div>
      } @else {
        <div class="alerts-grid" role="list" aria-label="Alert rules">
          @for (alert of alerts(); track alert.name) {
            <article class="alert-card surface" role="listitem">
              <div class="card-header">
                <h2 class="alert-name">{{ alert.name }}</h2>
                <app-status-badge
                  [status]="getAlertStatus(alert)"
                  [label]="getAlertStatus(alert)"
                />
              </div>

              <dl class="alert-details">
                <div class="detail-row">
                  <dt>Metric</dt>
                  <dd><code>{{ alert.metricName }}</code></dd>
                </div>

                <div class="detail-row">
                  <dt>Condition</dt>
                  <dd>{{ alert.condition ?? 'Threshold breached' }} {{ alert.threshold ?? '' }}</dd>
                </div>

                <div class="detail-row">
                  <dt>Window</dt>
                  <dd>{{ alert.window }}</dd>
                </div>

                @if ((alert.actions?.length ?? 0) > 0) {
                  <div class="detail-row">
                    <dt>Actions</dt>
                    <dd>
                      <ul class="actions-list" role="list">
                        @for (action of alert.actions; track action) {
                          <li class="action-tag">{{ action }}</li>
                        }
                      </ul>
                    </dd>
                  </div>
                }
              </dl>

              <div class="card-actions">
                <button
                  class="action-button test"
                  (click)="testAlert(alert.name)"
                  [disabled]="testingAlerts().has(alert.name)"
                  [attr.aria-busy]="testingAlerts().has(alert.name)"
                  [attr.aria-label]="'Test ' + alert.name + ' alert'"
                  type="button">
                  @if (testingAlerts().has(alert.name)) {
                    <span class="button-spinner" aria-hidden="true"></span>
                    <span>Testing...</span>
                  } @else {
                    <span>Test Alert</span>
                  }
                </button>

                <button
                  class="action-button silence"
                  (click)="silenceAlert(alert.name)"
                  [disabled]="silencingAlerts().has(alert.name)"
                  [attr.aria-busy]="silencingAlerts().has(alert.name)"
                  [attr.aria-label]="'Silence ' + alert.name + ' alert for 1 hour'"
                  type="button">
                  @if (silencingAlerts().has(alert.name)) {
                    <span class="button-spinner" aria-hidden="true"></span>
                    <span>Silencing...</span>
                  } @else {
                    <span>Silence (1h)</span>
                  }
                </button>
              </div>

              @if (alertMessages().get(alert.name)) {
                <div class="alert-message" role="status" aria-live="polite">
                  {{ alertMessages().get(alert.name) }}
                </div>
              }
            </article>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .alerts-page {
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

    .loading-spinner, .button-spinner {
      width: 32px;
      height: 32px;
      border: 3px solid var(--border);
      border-top-color: var(--accent);
      border-radius: 50%;
      animation: spin 1s linear infinite;
    }

    .button-spinner {
      width: 14px;
      height: 14px;
      border-width: 2px;
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

    .alerts-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(400px, 1fr));
      gap: 1.5rem;

      @media (max-width: 768px) {
        grid-template-columns: 1fr;
      }
    }

    .alert-card {
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
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 1rem;
    }

    .alert-name {
      font-size: 18px;
      font-weight: 600;
      color: var(--text-primary);
      margin: 0;
    }

    .alert-details {
      margin: 0;
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }

    .detail-row {
      display: flex;
      gap: 1rem;

      dt {
        min-width: 80px;
        font-size: 13px;
        font-weight: 600;
        color: var(--text-secondary);
        text-transform: uppercase;
        letter-spacing: 0.05em;
      }

      dd {
        margin: 0;
        font-size: 14px;
        color: var(--text-primary);

        code {
          background: var(--bg-elevated);
          padding: 2px 6px;
          border-radius: 3px;
          font-family: 'Monaco', 'Courier New', monospace;
          font-size: 12px;
          color: var(--accent);
        }
      }
    }

    .actions-list {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }

    .action-tag {
      background: var(--bg-elevated);
      color: var(--text-secondary);
      padding: 0.25rem 0.5rem;
      border-radius: 4px;
      font-size: 11px;
      font-weight: 500;
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    .card-actions {
      display: flex;
      gap: 0.75rem;
      padding-top: 0.5rem;
      border-top: 1px solid var(--border);
    }

    .action-button {
      flex: 1;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 0.5rem;
      padding: 0.625rem 1rem;
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      font-weight: 500;
      font-size: 13px;
      cursor: pointer;
      transition: all var(--transition);

      &.test {
        background: var(--accent);
        color: var(--bg-primary);
        border-color: var(--accent);

        &:hover:not(:disabled) {
          background: var(--accent-hover);
        }
      }

      &.silence {
        background: transparent;
        color: var(--text-secondary);

        &:hover:not(:disabled) {
          background: rgba(245, 158, 11, 0.1);
          color: var(--warning);
          border-color: var(--warning);
        }
      }

      &:focus-visible {
        outline: 2px solid var(--accent);
        outline-offset: 2px;
      }

      &:disabled {
        opacity: 0.5;
        cursor: not-allowed;
      }
    }

    .alert-message {
      padding: 0.75rem;
      background: rgba(20, 184, 166, 0.1);
      border: 1px solid rgba(20, 184, 166, 0.3);
      border-radius: var(--radius-sm);
      font-size: 13px;
      color: var(--accent);
    }
  `]
})
export class AlertsComponent implements OnInit {
  private alertsService = inject(AlertsService);

  alerts = signal<AlertRule[]>([]);
  isLoading = signal(false);
  error = signal<string | null>(null);
  testingAlerts = signal<Set<string>>(new Set());
  silencingAlerts = signal<Set<string>>(new Set());
  alertMessages = signal<Map<string, string>>(new Map());

  ngOnInit(): void {
    this.loadAlerts();
  }

  loadAlerts(): void {
    this.isLoading.set(true);
    this.error.set(null);

    this.alertsService.getAlerts().subscribe({
      next: (rules) => {
        this.alerts.set(rules);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.error.set(err.error?.message || err.message || 'Failed to load alerts');
        this.isLoading.set(false);
      }
    });
  }

  testAlert(alertName: string): void {
    this.testingAlerts.update(set => new Set(set).add(alertName));
    this.clearMessage(alertName);

    this.alertsService.testAlert(alertName).subscribe({
      next: () => {
        this.testingAlerts.update(set => {
          const newSet = new Set(set);
          newSet.delete(alertName);
          return newSet;
        });
        this.setMessage(alertName, 'Test alert fired successfully');
      },
      error: (err) => {
        this.testingAlerts.update(set => {
          const newSet = new Set(set);
          newSet.delete(alertName);
          return newSet;
        });
        this.setMessage(alertName, `Test failed: ${err.error?.message || err.message}`);
      }
    });
  }

  silenceAlert(alertName: string): void {
    this.silencingAlerts.update(set => new Set(set).add(alertName));
    this.clearMessage(alertName);

    this.alertsService.silenceAlert(alertName, '1h').subscribe({
      next: () => {
        this.silencingAlerts.update(set => {
          const newSet = new Set(set);
          newSet.delete(alertName);
          return newSet;
        });
        this.setMessage(alertName, 'Alert silenced for 1 hour');
      },
      error: (err) => {
        this.silencingAlerts.update(set => {
          const newSet = new Set(set);
          newSet.delete(alertName);
          return newSet;
        });
        this.setMessage(alertName, `Silence failed: ${err.error?.message || err.message}`);
      }
    });
  }

  getAlertStatus(alert: AlertRule): 'healthy' | 'degraded' | 'unknown' {
    // In real implementation, this would check actual alert state
    return (alert.actions?.length ?? 0) > 0 ? 'healthy' : 'degraded';
  }

  private setMessage(alertName: string, message: string): void {
    this.alertMessages.update(map => {
      const newMap = new Map(map);
      newMap.set(alertName, message);
      return newMap;
    });

    // Clear message after 5 seconds
    setTimeout(() => this.clearMessage(alertName), 5000);
  }

  private clearMessage(alertName: string): void {
    this.alertMessages.update(map => {
      const newMap = new Map(map);
      newMap.delete(alertName);
      return newMap;
    });
  }
}
