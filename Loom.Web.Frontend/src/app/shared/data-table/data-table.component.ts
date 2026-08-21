import { Component, Input, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
export interface TableColumn {
  key: string;
  label: string;
  sortable?: boolean;
}

export interface TableRow {
  [key: string]: any;
}

type SortDirection = 'asc' | 'desc' | null;

@Component({
  selector: 'app-data-table',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="table-container" role="region" [attr.aria-label]="ariaLabel || 'Data table'" tabindex="0">
      <table class="data-table">
        <thead>
          <tr>
            @for (column of columns; track column.key) {
              <th
                [attr.scope]="'col'"
                [class.sortable]="column.sortable"
                (click)="column.sortable && toggleSort(column.key)"
                (keydown.enter)="column.sortable && toggleSort(column.key)"
                (keydown.space)="column.sortable && toggleSort(column.key); $event.preventDefault()"
                [attr.tabindex]="column.sortable ? 0 : undefined"
                [attr.aria-sort]="getSortAriaLabel(column.key)"
                [attr.role]="column.sortable ? 'button' : undefined">
                {{ column.label }}
                @if (column.sortable && sortColumn() === column.key) {
                  <span class="sort-indicator" aria-hidden="true">
                    {{ sortDirection() === 'asc' ? '▲' : '▼' }}
                  </span>
                }
              </th>
            }
          </tr>
        </thead>
        <tbody>
          @if (sortedData().length === 0) {
            <tr>
              <td [attr.colspan]="columns.length" class="empty-state">
                {{ emptyMessage || 'No data available' }}
              </td>
            </tr>
          } @else {
            @for (row of sortedData(); track $index) {
              <tr [class.highlighted]="isHighlighted(row)">
                @for (column of columns; track column.key) {
                  <td>{{ row[column.key] ?? '-' }}</td>
                }
              </tr>
            }
          }
        </tbody>
      </table>
    </div>
  `,
  styles: [`
    .table-container {
      background: var(--bg-surface);
      border: 1px solid var(--border);
      border-radius: var(--radius-md);
      overflow-x: auto;
      max-height: 600px;
      overflow-y: auto;

      /* WCAG: Focus indicator for keyboard nav */
      &:focus {
        outline: 2px solid var(--accent);
        outline-offset: -2px;
      }
    }

    .data-table {
      width: 100%;
      border-collapse: collapse;
      font-size: 13px;
    }

    thead {
      position: sticky;
      top: 0;
      background: var(--bg-elevated);
      z-index: 10;
      border-bottom: 2px solid var(--border);
    }

    th {
      text-align: left;
      padding: 0.875rem 1rem;
      font-weight: 600;
      color: var(--text-secondary);
      text-transform: uppercase;
      letter-spacing: 0.05em;
      font-size: 11px;
      white-space: nowrap;

      &.sortable {
        cursor: pointer;
        user-select: none;
        transition: all var(--transition);

        &:hover {
          color: var(--accent);
          background: rgba(20, 184, 166, 0.05);
        }

        /* WCAG: Focus indicator */
        &:focus {
          outline: 2px solid var(--accent);
          outline-offset: -2px;
        }
      }
    }

    .sort-indicator {
      margin-left: 0.5rem;
      color: var(--accent);
      font-size: 10px;
    }

    tbody tr {
      border-bottom: 1px solid var(--border);
      transition: background var(--transition);

      &:hover {
        background: rgba(20, 184, 166, 0.05);
      }

      &:last-child {
        border-bottom: none;
      }

      &.highlighted {
        background: rgba(20, 184, 166, 0.12);
        box-shadow: inset 3px 0 0 var(--accent);

        &:hover {
          background: rgba(20, 184, 166, 0.18);
        }
      }
    }

    td {
      padding: 0.875rem 1rem;
      color: var(--text-primary);
    }

    .empty-state {
      text-align: center;
      color: var(--text-muted);
      padding: 3rem 1rem !important;
      font-style: italic;
    }

    @media (max-width: 768px) {
      th, td {
        padding: 0.625rem 0.75rem;
      }
    }
  `]
})
export class DataTableComponent {
  @Input({ required: true }) columns!: TableColumn[];
  @Input({ required: true }) data!: TableRow[];
  @Input() emptyMessage?: string;
  @Input() ariaLabel?: string;
  @Input() highlightPredicate?: (row: TableRow) => boolean;

  sortColumn = signal<string | null>(null);
  sortDirection = signal<SortDirection>(null);

  sortedData = signal<TableRow[]>([]);

  isHighlighted(row: TableRow): boolean {
    return this.highlightPredicate ? this.highlightPredicate(row) : false;
  }

  ngOnChanges(): void {
    this.applySort();
  }

  toggleSort(columnKey: string): void {
    if (this.sortColumn() === columnKey) {
      // Cycle: asc -> desc -> null
      if (this.sortDirection() === 'asc') {
        this.sortDirection.set('desc');
      } else if (this.sortDirection() === 'desc') {
        this.sortDirection.set(null);
        this.sortColumn.set(null);
      }
    } else {
      this.sortColumn.set(columnKey);
      this.sortDirection.set('asc');
    }

    this.applySort();
  }

  getSortAriaLabel(columnKey: string): string | null {
    if (this.sortColumn() !== columnKey) {
      return null;
    }
    return this.sortDirection() === 'asc' ? 'ascending' : 'descending';
  }

  private applySort(): void {
    if (!this.sortColumn() || !this.sortDirection()) {
      this.sortedData.set([...this.data]);
      return;
    }

    const sorted = [...this.data].sort((a, b) => {
      const aVal = a[this.sortColumn()!];
      const bVal = b[this.sortColumn()!];

      if (aVal == null && bVal == null) return 0;
      if (aVal == null) return 1;
      if (bVal == null) return -1;

      const comparison = aVal < bVal ? -1 : aVal > bVal ? 1 : 0;
      return this.sortDirection() === 'asc' ? comparison : -comparison;
    });

    this.sortedData.set(sorted);
  }
}
