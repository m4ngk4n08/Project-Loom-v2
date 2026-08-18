import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { QueryService, QueryResponse } from '../../core/services/query.service';
import { DataTableComponent, TableColumn, TableRow } from '../../shared/data-table/data-table.component';

@Component({
  selector: 'app-query-builder',
  standalone: true,
  imports: [CommonModule, FormsModule, DataTableComponent],
  template: `
    <div class="query-builder-page">
      <header class="page-header">
        <h1 class="page-title">Query Builder</h1>
        <p class="page-subtitle">Execute LoomQL queries against your telemetry data</p>
      </header>

      <div class="query-section surface" role="region" aria-label="Query editor">
        <div class="editor-header">
          <label for="query-editor" class="editor-label">LoomQL Query</label>
          <button
            class="help-button"
            type="button"
            (click)="showExamples = !showExamples"
            [attr.aria-expanded]="showExamples"
            aria-controls="query-examples">
            <span aria-hidden="true">?</span>
            <span class="sr-only">Toggle query examples</span>
          </button>
        </div>

        <textarea
          id="query-editor"
          class="query-editor"
          [(ngModel)]="query"
          (keydown.ctrl.enter)="executeQuery()"
          [attr.aria-describedby]="showExamples ? 'query-examples' : undefined"
          placeholder="SELECT * FROM metrics WHERE name = 'cpu.usage' AND timestamp > NOW() - 1h"
          rows="8"
          spellcheck="false">
        </textarea>

        @if (showExamples) {
          <aside
            id="query-examples"
            class="examples-panel"
            role="complementary"
            aria-label="Query examples">
            <h3 class="examples-title">Example Queries</h3>
            <ul class="examples-list" role="list">
              <li>
                <button
                  class="example-button"
                  (click)="useExample(examples[0])"
                  type="button">
                  <code>{{ examples[0] }}</code>
                </button>
              </li>
              <li>
                <button
                  class="example-button"
                  (click)="useExample(examples[1])"
                  type="button">
                  <code>{{ examples[1] }}</code>
                </button>
              </li>
              <li>
                <button
                  class="example-button"
                  (click)="useExample(examples[2])"
                  type="button">
                  <code>{{ examples[2] }}</code>
                </button>
              </li>
            </ul>
          </aside>
        }

        <div class="editor-footer">
          <button
            class="execute-button"
            (click)="executeQuery()"
            [disabled]="!query.trim() || isExecuting()"
            [attr.aria-busy]="isExecuting()"
            type="button">
            @if (isExecuting()) {
              <span class="button-spinner" aria-hidden="true"></span>
              <span>Executing...</span>
            } @else {
              <span>Execute Query</span>
              <span class="kbd-hint" aria-hidden="true">Ctrl+Enter</span>
            }
          </button>

          @if (executionTime() !== null) {
            <span class="execution-time" role="status" aria-live="polite">
              Executed in {{ executionTime() }}ms
            </span>
          }
        </div>
      </div>

      @if (error()) {
        <div class="error-panel" role="alert" aria-live="assertive">
          <span class="error-icon" aria-hidden="true">⚠</span>
          <div class="error-content">
            <strong>Query Error</strong>
            <p>{{ error() }}</p>
          </div>
        </div>
      }

      @if (results()) {
        <section class="results-section" role="region" aria-label="Query results">
          <div class="results-header">
            <h2 class="results-title">Results</h2>
            <span class="results-count" [attr.aria-label]="results()!.rows.length + ' rows returned'">
              {{ results()!.rows.length }} rows
            </span>
          </div>

          <app-data-table
            [columns]="tableColumns()"
            [data]="tableData()"
            emptyMessage="Query returned no results"
            ariaLabel="Query results table"
          />
        </section>
      }
    </div>
  `,
  styles: [`
    .query-builder-page {
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

    .query-section {
      padding: 1.5rem;
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }

    .editor-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
    }

    .editor-label {
      font-size: 14px;
      font-weight: 600;
      color: var(--text-secondary);
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    .help-button {
      width: 24px;
      height: 24px;
      border-radius: 50%;
      background: var(--bg-elevated);
      border: 1px solid var(--border);
      color: var(--accent);
      font-weight: 700;
      cursor: pointer;
      transition: all var(--transition);

      &:hover {
        background: var(--accent);
        color: var(--bg-primary);
        border-color: var(--accent);
      }

      &:focus-visible {
        outline: 2px solid var(--accent);
        outline-offset: 2px;
      }
    }

    .query-editor {
      width: 100%;
      background: var(--bg-primary);
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      padding: 1rem;
      color: var(--text-primary);
      font-family: 'Monaco', 'Courier New', monospace;
      font-size: 13px;
      line-height: 1.6;
      resize: vertical;
      transition: border-color var(--transition);

      &:focus {
        outline: none;
        border-color: var(--accent);
        box-shadow: 0 0 0 3px rgba(20, 184, 166, 0.1);
      }

      &::placeholder {
        color: var(--text-muted);
        font-style: italic;
      }
    }

    .examples-panel {
      background: var(--bg-elevated);
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      padding: 1rem;
    }

    .examples-title {
      font-size: 12px;
      font-weight: 600;
      color: var(--text-secondary);
      text-transform: uppercase;
      letter-spacing: 0.05em;
      margin: 0 0 0.75rem;
    }

    .examples-list {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
    }

    .example-button {
      width: 100%;
      text-align: left;
      padding: 0.75rem;
      background: var(--bg-primary);
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      cursor: pointer;
      transition: all var(--transition);

      code {
        color: var(--accent);
        font-size: 12px;
        word-break: break-all;
      }

      &:hover {
        border-color: var(--accent);
        background: rgba(20, 184, 166, 0.05);
      }

      &:focus-visible {
        outline: 2px solid var(--accent);
        outline-offset: 2px;
      }
    }

    .editor-footer {
      display: flex;
      align-items: center;
      gap: 1rem;
      padding-top: 0.5rem;
      border-top: 1px solid var(--border);
    }

    .execute-button {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      padding: 0.75rem 1.5rem;
      background: var(--accent);
      color: var(--bg-primary);
      border: none;
      border-radius: var(--radius-sm);
      font-weight: 600;
      font-size: 14px;
      cursor: pointer;
      transition: all var(--transition);

      &:hover:not(:disabled) {
        background: var(--accent-hover);
        transform: translateY(-1px);
        box-shadow: var(--shadow-md);
      }

      &:focus-visible {
        outline: 2px solid var(--accent);
        outline-offset: 2px;
      }

      &:disabled {
        opacity: 0.5;
        cursor: not-allowed;
      }

      .kbd-hint {
        font-size: 11px;
        opacity: 0.8;
        padding: 2px 6px;
        background: rgba(0, 0, 0, 0.2);
        border-radius: 3px;
      }
    }

    .button-spinner {
      width: 14px;
      height: 14px;
      border: 2px solid rgba(255, 255, 255, 0.3);
      border-top-color: white;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }

    .execution-time {
      font-size: 13px;
      color: var(--text-muted);
    }

    .error-panel {
      display: flex;
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

    .results-section {
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }

    .results-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
    }

    .results-title {
      font-size: 18px;
      font-weight: 600;
      color: var(--text-primary);
      margin: 0;
    }

    .results-count {
      background: var(--bg-elevated);
      color: var(--accent);
      padding: 0.375rem 0.75rem;
      border-radius: var(--radius-sm);
      font-size: 12px;
      font-weight: 600;
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
export class QueryBuilderComponent {
  private queryService = inject(QueryService);

  query = '';
  showExamples = false;
  isExecuting = signal(false);
  results = signal<QueryResponse | null>(null);
  error = signal<string | null>(null);
  executionTime = signal<number | null>(null);

  tableColumns = signal<TableColumn[]>([]);
  tableData = signal<TableRow[]>([]);

  examples = [
    `SELECT * FROM metrics WHERE name = 'cpu.usage' AND timestamp > NOW() - 1h`,
    `SELECT name, AVG(value) FROM metrics WHERE timestamp > NOW() - 24h GROUP BY name`,
    `SELECT * FROM metrics WHERE name LIKE 'request.%' ORDER BY timestamp DESC LIMIT 100`
  ];

  executeQuery(): void {
    if (!this.query.trim() || this.isExecuting()) return;

    this.isExecuting.set(true);
    this.error.set(null);
    this.results.set(null);
    this.executionTime.set(null);

    this.queryService.execute(this.query).subscribe({
      next: (response) => {
        this.results.set(response);
        this.executionTime.set(response.executionTimeMs);
        this.transformResults(response);
        this.isExecuting.set(false);
      },
      error: (err) => {
        this.error.set(err.error?.message || err.message || 'Query execution failed');
        this.isExecuting.set(false);
      }
    });
  }

  useExample(example: string): void {
    this.query = example;
    this.showExamples = false;
  }

  private transformResults(response: QueryResponse): void {
    // Build table columns from response
    const columns: TableColumn[] = response.columns.map(col => ({
      key: col,
      label: col,
      sortable: true
    }));
    this.tableColumns.set(columns);

    // Build table rows
    const rows: TableRow[] = response.rows.map(row => {
      const rowData: TableRow = {};
      response.columns.forEach((col, idx) => {
        const value = row.values[idx];
        rowData[col] = this.formatValue(value);
      });
      return rowData;
    });
    this.tableData.set(rows);
  }

  private formatValue(value: any): string {
    if ('text' in value) return value.text;
    if ('number' in value) return value.number.toFixed(2);
    if ('boolean' in value) return value.boolean.toString();
    if ('timestamp' in value) return new Date(value.timestamp).toLocaleString();
    if ('null' in value) return '-';
    return String(value);
  }
}
