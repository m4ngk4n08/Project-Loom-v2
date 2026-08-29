import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DashboardStateService } from '../../core/services/dashboard-state.service';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-top-bar',
  standalone: true,
  imports: [CommonModule],
  template: `
    <header class="top-bar" role="banner">
      <div class="top-bar-left">
        <h1 class="logo">
          <span class="logo-icon" role="img" aria-label="Loom logo">🧵</span>
          <span class="logo-text">Loom</span>
          <span class="version" aria-label="Version 2.0">v2</span>
        </h1>
      </div>

      <div class="top-bar-right">
        <!-- Connection Status -->
        <div
          class="connection-status"
          [class.connected]="stateService.isConnected()"
          role="status"
          [attr.aria-live]="'polite'"
          [attr.aria-label]="stateService.isConnected() ? 'WebSocket connected' : 'WebSocket disconnected'">
          <span
            class="status-dot"
            [class.active]="stateService.isConnected()"
            role="img"
            [attr.aria-label]="stateService.isConnected() ? 'Connected indicator' : 'Disconnected indicator'">
          </span>
          <span class="status-label">
            {{ stateService.isConnected() ? 'Connected' : 'Disconnected' }}
          </span>
        </div>

        <button type="button" class="logout" (click)="auth.logout()">Sign out</button>
      </div>
    </header>
  `,
  styles: [`
    .top-bar {
      height: var(--topbar-height);
      background: var(--bg-surface);
      border-bottom: 1px solid var(--border);
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 0 1.5rem;
    }

    .top-bar-left {
      display: flex;
      align-items: center;
    }

    .logo {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      font-size: 20px;
      font-weight: 600;
      color: var(--text-primary);
      margin: 0;
    }

    .logo-icon {
      font-size: 24px;
    }

    .version {
      font-size: 11px;
      font-weight: 500;
      color: var(--text-muted);
      background: var(--bg-elevated);
      padding: 2px 6px;
      border-radius: 4px;
      margin-left: 0.25rem;
    }

    .top-bar-right {
      display: flex;
      align-items: center;
      gap: 1rem;
    }

    .connection-status {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      padding: 0.5rem 0.75rem;
      background: var(--bg-elevated);
      border-radius: var(--radius-sm);
      font-size: 13px;
      color: var(--text-secondary);
      border: 1px solid var(--border);
    }

    .status-dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: var(--text-muted);
      transition: background var(--transition);

      &.active {
        background: var(--accent);
        box-shadow: 0 0 8px rgba(20, 184, 166, 0.6);
        animation: pulse 2s infinite;
      }
    }

    @keyframes pulse {
      0%, 100% {
        opacity: 1;
      }
      50% {
        opacity: 0.7;
      }
    }

    .status-label {
      font-weight: 500;
    }

    .logout {
      padding: 0.5rem 0.75rem;
      background: var(--bg-elevated);
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      color: var(--text-secondary);
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;

      &:hover {
        color: var(--text-primary);
        border-color: var(--text-muted);
      }
    }

    .connection-status.connected .status-label {
      color: var(--accent);
    }

    @media (max-width: 768px) {
      .top-bar {
        padding: 0 1rem;
      }

      .logo-text {
        display: none;
      }

      .status-label {
        display: none;
      }
    }
  `]
})
export class TopBarComponent {
  stateService = inject(DashboardStateService);
  protected readonly auth = inject(AuthService);
}
