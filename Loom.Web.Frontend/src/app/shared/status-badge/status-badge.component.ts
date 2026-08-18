import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

export type StatusType = 'healthy' | 'unhealthy' | 'degraded' | 'unknown';

@Component({
  selector: 'app-status-badge',
  standalone: true,
  imports: [CommonModule],
  template: `
    <span
      class="status-badge"
      [class.success]="status === 'healthy'"
      [class.danger]="status === 'unhealthy'"
      [class.warning]="status === 'degraded'"
      [class.muted]="status === 'unknown'"
      [attr.role]="'status'"
      [attr.aria-label]="ariaLabel || label + ' status: ' + status">
      <span
        class="status-dot"
        role="img"
        [attr.aria-label]="status + ' indicator'">
      </span>
      <span class="status-label">{{ label }}</span>
    </span>
  `,
  styles: [`
    .status-badge {
      display: inline-flex;
      align-items: center;
      gap: 0.5rem;
      padding: 0.375rem 0.75rem;
      background: var(--bg-elevated);
      border-radius: var(--radius-sm);
      font-size: 13px;
      font-weight: 500;
      border: 1px solid var(--border);
      transition: all var(--transition);

      /* WCAG: Ensure proper contrast ratios */
      &.success {
        border-color: rgba(16, 185, 129, 0.3);
        background: rgba(16, 185, 129, 0.1);
        color: var(--success);
      }

      &.danger {
        border-color: rgba(239, 68, 68, 0.3);
        background: rgba(239, 68, 68, 0.1);
        color: var(--danger);
      }

      &.warning {
        border-color: rgba(245, 158, 11, 0.3);
        background: rgba(245, 158, 11, 0.1);
        color: var(--warning);
      }

      &.muted {
        color: var(--text-muted);
      }
    }

    .status-dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      flex-shrink: 0;
    }

    .status-badge.success .status-dot {
      background: var(--success);
      box-shadow: 0 0 8px rgba(16, 185, 129, 0.5);
    }

    .status-badge.danger .status-dot {
      background: var(--danger);
      box-shadow: 0 0 8px rgba(239, 68, 68, 0.5);
    }

    .status-badge.warning .status-dot {
      background: var(--warning);
      box-shadow: 0 0 8px rgba(245, 158, 11, 0.5);
    }

    .status-badge.muted .status-dot {
      background: var(--text-muted);
    }

    .status-label {
      white-space: nowrap;
    }
  `]
})
export class StatusBadgeComponent {
  @Input({ required: true }) status!: StatusType;
  @Input({ required: true }) label!: string;
  @Input() ariaLabel?: string;
}
