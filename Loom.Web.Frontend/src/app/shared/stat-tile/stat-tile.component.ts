import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

export type TrendDirection = 'up' | 'down' | 'neutral';

@Component({
  selector: 'app-stat-tile',
  standalone: true,
  imports: [CommonModule],
  template: `
    <article class="stat-tile surface" [attr.aria-label]="label + ' statistic'">
      <div class="stat-header">
        <h3 class="stat-label">{{ label }}</h3>
        @if (trend) {
          <span
            class="trend-indicator"
            [class.up]="trend === 'up'"
            [class.down]="trend === 'down'"
            [class.neutral]="trend === 'neutral'"
            [attr.aria-label]="'Trend: ' + trend"
            role="img">
            @if (trend === 'up') { ▲ }
            @if (trend === 'down') { ▼ }
            @if (trend === 'neutral') { ● }
          </span>
        }
      </div>
      <div class="stat-value" [attr.aria-live]="'polite'">
        {{ value }}
        @if (unit) {
          <span class="stat-unit">{{ unit }}</span>
        }
      </div>
      @if (subtitle) {
        <div class="stat-subtitle">{{ subtitle }}</div>
      }
    </article>
  `,
  styles: [`
    .stat-tile {
      padding: 1.25rem;
      min-height: 120px;
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      transition: all var(--transition);

      &:hover {
        border-color: rgba(20, 184, 166, 0.3);
        transform: translateY(-2px);
        box-shadow: var(--shadow-md);
      }
    }

    .stat-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
    }

    .stat-label {
      font-size: 13px;
      font-weight: 500;
      color: var(--text-secondary);
      margin: 0;
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    .trend-indicator {
      font-size: 12px;
      font-weight: 700;

      &.up {
        color: var(--success);
      }

      &.down {
        color: var(--danger);
      }

      &.neutral {
        color: var(--text-muted);
      }
    }

    .stat-value {
      font-size: 32px;
      font-weight: 700;
      color: var(--text-primary);
      line-height: 1.2;
      flex: 1;
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }

    .stat-unit {
      font-size: 18px;
      font-weight: 500;
      color: var(--text-muted);
    }

    .stat-subtitle {
      font-size: 12px;
      color: var(--text-muted);
    }

    @media (max-width: 768px) {
      .stat-tile {
        min-height: 100px;
        padding: 1rem;
      }

      .stat-value {
        font-size: 24px;
      }

      .stat-unit {
        font-size: 14px;
      }
    }
  `]
})
export class StatTileComponent {
  @Input({ required: true }) label!: string;
  @Input({ required: true }) value!: string | number;
  @Input() unit?: string;
  @Input() subtitle?: string;
  @Input() trend?: TrendDirection;
}
