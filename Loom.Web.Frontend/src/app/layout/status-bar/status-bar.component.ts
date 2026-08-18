import { Component, inject, signal, OnInit, DestroyRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ExportersService } from '../../core/services/exporters.service';
import { interval } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

interface ExporterHealth {
  name: string;
  isHealthy: boolean;
}

@Component({
  selector: 'app-status-bar',
  standalone: true,
  imports: [CommonModule],
  template: `
    <footer class="status-bar" role="contentinfo">
      <div class="status-section">
        <span class="status-label" aria-label="System uptime">
          Uptime: <strong>{{ uptime() }}</strong>
        </span>
      </div>

      <div class="status-section exporters" role="status" aria-live="polite" aria-label="Exporter health status">
        @for (exporter of exporterHealth(); track exporter.name) {
          <span
            class="exporter-badge"
            [class.healthy]="exporter.isHealthy"
            [class.unhealthy]="!exporter.isHealthy"
            [attr.aria-label]="exporter.name + ' exporter ' + (exporter.isHealthy ? 'healthy' : 'unhealthy')">
            <span
              class="badge-dot"
              role="img"
              [attr.aria-label]="exporter.isHealthy ? 'Healthy indicator' : 'Unhealthy indicator'">
            </span>
            <span class="badge-label">{{ exporter.name }}</span>
          </span>
        }
      </div>
    </footer>
  `,
  styles: [`
    .status-bar {
      height: var(--statusbar-height);
      background: var(--bg-surface);
      border-top: 1px solid var(--border);
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 0 1.5rem;
      font-size: 12px;
      color: var(--text-secondary);
    }

    .status-section {
      display: flex;
      align-items: center;
      gap: 1rem;
    }

    .status-label {
      strong {
        color: var(--text-primary);
        font-weight: 500;
      }
    }

    .exporters {
      display: flex;
      gap: 0.5rem;
    }

    .exporter-badge {
      display: flex;
      align-items: center;
      gap: 0.375rem;
      padding: 0.25rem 0.5rem;
      background: var(--bg-elevated);
      border-radius: 4px;
      border: 1px solid var(--border);
      font-weight: 500;
      transition: all var(--transition);

      /* WCAG: Ensure sufficient contrast */
      &.healthy {
        border-color: rgba(16, 185, 129, 0.3);
        background: rgba(16, 185, 129, 0.1);

        .badge-dot {
          background: var(--success);
        }

        .badge-label {
          color: var(--success);
        }
      }

      &.unhealthy {
        border-color: rgba(239, 68, 68, 0.3);
        background: rgba(239, 68, 68, 0.1);

        .badge-dot {
          background: var(--danger);
        }

        .badge-label {
          color: var(--danger);
        }
      }
    }

    .badge-dot {
      width: 6px;
      height: 6px;
      border-radius: 50%;
      flex-shrink: 0;
    }

    .badge-label {
      font-size: 11px;
      text-transform: uppercase;
      letter-spacing: 0.025em;
    }

    @media (max-width: 768px) {
      .status-bar {
        padding: 0 1rem;
        font-size: 11px;
      }

      .badge-label {
        display: none;
      }
    }
  `]
})
export class StatusBarComponent implements OnInit {
  private exportersService = inject(ExportersService);
  private destroyRef = inject(DestroyRef);

  uptime = signal('0h 0m');
  exporterHealth = signal<ExporterHealth[]>([]);

  private startTime = Date.now();

  ngOnInit(): void {
    this.updateUptime();
    this.loadExporterHealth();

    // Update uptime every 10 seconds
    interval(10000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.updateUptime());

    // Refresh exporter health every 30 seconds
    interval(30000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.loadExporterHealth());
  }

  private updateUptime(): void {
    const elapsed = Date.now() - this.startTime;
    const hours = Math.floor(elapsed / 3600000);
    const minutes = Math.floor((elapsed % 3600000) / 60000);
    this.uptime.set(`${hours}h ${minutes}m`);
  }

  private loadExporterHealth(): void {
    this.exportersService.getStatus().subscribe({
      next: (status) => {
        this.exporterHealth.set(
          status.map(s => ({
            name: s.name,
            isHealthy: s.isHealthy
          }))
        );
      },
      error: () => {
        // Silently fail - status bar shouldn't block UI
        this.exporterHealth.set([]);
      }
    });
  }
}
