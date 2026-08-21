import {
  Component,
  ElementRef,
  ViewChild,
  inject,
  signal,
  OnInit
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { QueryService, QueryResponse } from '../../core/services/query.service';
import { QueryHistoryService } from '../../core/services/query-history.service';
import { MetricsExplorerService } from '../../core/services/metrics-explorer.service';
import { DataTableComponent, TableColumn, TableRow } from '../../shared/data-table/data-table.component';

interface Suggestion {
  label: string;
  insert: string;
  type: 'keyword' | 'method' | 'aggregate' | 'column' | 'value';
}

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
          <div class="editor-actions">
            <button
              class="history-button"
              type="button"
              (click)="toggleHistory()"
              [attr.aria-expanded]="showHistory()"
              aria-controls="query-history">
              History
              <span class="history-count" aria-hidden="true">{{ historyService.history().length }}</span>
            </button>
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
        </div>

        @if (showHistory()) {
          <aside
            id="query-history"
            class="history-panel"
            role="complementary"
            aria-label="Query history">
            @if (historyService.history().length > 0) {
              <ul class="history-list" role="list">
                @for (item of historyService.history(); track item) {
                  <li>
                    <button
                      class="history-item"
                      type="button"
                      (click)="useHistoryItem(item)">
                      <code>{{ item }}</code>
                    </button>
                  </li>
                }
              </ul>
              <button class="history-clear" type="button" (click)="historyService.clear()">
                Clear history
              </button>
            } @else {
              <p class="history-empty">No recent queries yet.</p>
            }
          </aside>
        }

        <div class="editor-wrapper">
          <textarea
            #queryEditor
            id="query-editor"
            class="query-editor"
            [(ngModel)]="query"
            (ngModelChange)="onQueryChange()"
            (keydown)="onKeydown($event)"
            (focus)="updateSuggestions()"
            (blur)="closeSuggestions()"
            [attr.aria-describedby]="showExamples ? 'query-examples' : undefined"
            [attr.aria-expanded]="suggestions().length > 0"
            [attr.aria-controls]="suggestions().length > 0 ? 'query-suggestions' : undefined"
            [attr.aria-activedescendant]="activeSuggestion() >= 0 ? 'query-suggestion-' + activeSuggestion() : undefined"
            placeholder="SELECT * FROM telemetry LIMIT 100"
            rows="8"
            spellcheck="false">
          </textarea>

          @if (suggestions().length > 0) {
            <ul
              id="query-suggestions"
              class="suggestions-list"
              role="listbox"
              aria-label="Query suggestions">
              @for (suggestion of suggestions(); track $index) {
                <li
                  id="query-suggestion-{{ $index }}"
                  class="suggestion-item"
                  [class.active]="activeSuggestion() === $index"
                  role="option"
                  [attr.aria-selected]="activeSuggestion() === $index"
                  (mousedown)="acceptSuggestion(suggestion, $event)">
                  <span class="suggestion-type" aria-hidden="true">{{ suggestion.type }}</span>
                  <code class="suggestion-label">{{ suggestion.label }}</code>
                </li>
              }
            </ul>
          }
        </div>

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

    .editor-actions {
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }

    .history-button {
      display: inline-flex;
      align-items: center;
      gap: 0.4rem;
      background: var(--bg-elevated);
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      padding: 0.25rem 0.75rem;
      font-size: 12px;
      font-weight: 600;
      color: var(--text-secondary);
      cursor: pointer;
      transition: all var(--transition);

      &:hover {
        border-color: var(--accent);
        color: var(--accent);
      }

      &:focus-visible {
        outline: 2px solid var(--accent);
        outline-offset: 2px;
      }

      .history-count {
        background: rgba(20, 184, 166, 0.15);
        color: var(--accent);
        border-radius: 999px;
        padding: 1px 6px;
        font-size: 11px;
      }
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

    .history-panel {
      background: var(--bg-elevated);
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      padding: 1rem;
    }

    .history-list {
      list-style: none;
      margin: 0 0 0.75rem;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: 0.4rem;
      max-height: 240px;
      overflow-y: auto;
    }

    .history-item {
      width: 100%;
      text-align: left;
      padding: 0.5rem 0.75rem;
      background: var(--bg-primary);
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      cursor: pointer;
      transition: all var(--transition);

      code {
        color: var(--text-primary);
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

    .history-clear {
      background: transparent;
      border: none;
      color: var(--text-muted);
      font-size: 12px;
      cursor: pointer;
      padding: 0;

      &:hover {
        color: var(--danger);
      }
    }

    .history-empty {
      margin: 0;
      font-size: 13px;
      color: var(--text-muted);
    }

    .editor-wrapper {
      position: relative;
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

    .suggestions-list {
      position: absolute;
      left: 0;
      right: 0;
      top: 100%;
      margin: 4px 0 0;
      padding: 0.4rem;
      list-style: none;
      background: var(--bg-elevated);
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      box-shadow: var(--shadow-md);
      max-height: 220px;
      overflow-y: auto;
      z-index: 10;
    }

    .suggestion-item {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      padding: 0.4rem 0.6rem;
      border-radius: 4px;
      cursor: pointer;

      &.active {
        background: rgba(20, 184, 166, 0.12);
      }

      &:hover {
        background: rgba(20, 184, 166, 0.08);
      }
    }

    .suggestion-type {
      flex-shrink: 0;
      font-size: 10px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      color: var(--text-muted);
      min-width: 60px;
    }

    .suggestion-label {
      color: var(--accent);
      font-size: 12px;
      word-break: break-all;
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
export class QueryBuilderComponent implements OnInit {
  private queryService = inject(QueryService);
  historyService = inject(QueryHistoryService);
  private metricsExplorer = inject(MetricsExplorerService);

  @ViewChild('queryEditor', { static: false }) queryEditor!: ElementRef<HTMLTextAreaElement>;

  query = '';
  showExamples = false;
  showHistory = signal(false);
  isExecuting = signal(false);
  results = signal<QueryResponse | null>(null);
  error = signal<string | null>(null);
  executionTime = signal<number | null>(null);

  tableColumns = signal<TableColumn[]>([]);
  tableData = signal<TableRow[]>([]);

  suggestions = signal<Suggestion[]>([]);
  activeSuggestion = signal(-1);

  private methodNames: string[] = [];
  private historyIndex = -1;
  private queryBeforeRecall = '';

  examples = [
    `SELECT * FROM telemetry LIMIT 100`,
    `SELECT AVG(order.processing.duration) FROM telemetry WHERE method = 'order'`,
    `SELECT COUNT(*) FROM telemetry WHERE method = 'payment' ORDER BY method DESC`
  ];

  ngOnInit(): void {
    this.metricsExplorer.getMetricNames().subscribe({
      next: (names) => { this.methodNames = names ?? []; },
      error: () => { this.methodNames = []; }
    });
  }

  executeQuery(): void {
    if (!this.query.trim() || this.isExecuting()) return;

    this.historyService.add(this.query);
    this.historyIndex = -1;

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
    this.updateSuggestions();
  }

  useHistoryItem(item: string): void {
    this.query = item;
    this.showHistory.set(false);
    this.updateSuggestions();
    this.queryEditor.nativeElement.focus();
  }

  toggleHistory(): void {
    this.showHistory.update(v => !v);
  }

  onQueryChange(): void {
    this.historyIndex = -1;
    this.updateSuggestions();
  }

  updateSuggestions(): void {
    if (!this.queryEditor?.nativeElement) return;
    const list = this.buildSuggestions();
    this.suggestions.set(list);
    this.activeSuggestion.set(list.length > 0 ? 0 : -1);
  }

  closeSuggestions(): void {
    this.suggestions.set([]);
    this.activeSuggestion.set(-1);
  }

  acceptSuggestion(suggestion: Suggestion, event?: MouseEvent): void {
    if (event) event.preventDefault();

    const el = this.queryEditor.nativeElement;
    const range = this.currentTokenRange(el);
    const before = this.query.slice(0, range.start);
    const after = this.query.slice(range.end);

    // Replace the current token with the suggestion. If the user is mid-typed
    // inside a quoted method literal (token starts with a quote), the suggestion
    // already carries the full quoted value, so a direct swap is correct.
    this.query = before + suggestion.insert + after;

    // Move caret to end of the inserted suggestion.
    const pos = before.length + suggestion.insert.length;
    el.focus();
    el.setSelectionRange(pos, pos);
    this.updateSuggestions();
  }

  onKeydown(event: KeyboardEvent): void {
    const el = this.queryEditor.nativeElement;

    if (event.ctrlKey && event.key === 'ArrowUp') {
      event.preventDefault();
      this.recallHistory(-1);
      return;
    }
    if (event.ctrlKey && event.key === 'ArrowDown') {
      event.preventDefault();
      this.recallHistory(1);
      return;
    }
    if (event.ctrlKey && event.key === 'Enter') {
      event.preventDefault();
      this.executeQuery();
      return;
    }

    const open = this.suggestions().length > 0;
    if (open && event.key === 'ArrowDown') {
      event.preventDefault();
      const next = (this.activeSuggestion() + 1) % this.suggestions().length;
      this.activeSuggestion.set(next);
      return;
    }
    if (open && event.key === 'ArrowUp') {
      event.preventDefault();
      const count = this.suggestions().length;
      const next = (this.activeSuggestion() - 1 + count) % count;
      this.activeSuggestion.set(next);
      return;
    }
    if (open && (event.key === 'Enter' || event.key === 'Tab')) {
      const active = this.suggestions()[this.activeSuggestion()];
      if (active) {
        event.preventDefault();
        this.acceptSuggestion(active);
      }
      return;
    }
    if (open && event.key === 'Escape') {
      event.preventDefault();
      this.closeSuggestions();
      return;
    }
  }

  private recallHistory(direction: number): void {
    const history = this.historyService.history();
    if (history.length === 0) return;

    if (this.historyIndex === -1) {
      this.queryBeforeRecall = this.query;
      this.historyIndex = 0;
    } else {
      this.historyIndex += direction;
      if (this.historyIndex < 0) {
        this.historyIndex = -1;
        this.query = this.queryBeforeRecall;
        this.updateSuggestions();
        return;
      }
      if (this.historyIndex >= history.length) {
        this.historyIndex = history.length - 1;
      }
    }

    this.query = history[this.historyIndex];
    this.closeSuggestions();
  }

  private currentTokenRange(el: HTMLTextAreaElement): { start: number; end: number; text: string } {
    const caret = el.selectionStart ?? this.query.length;
    let start = caret;
    while (start > 0 && !/\s/.test(this.query[start - 1])) {
      start--;
    }
    let end = caret;
    while (end < this.query.length && !/\s/.test(this.query[end])) {
      end++;
    }
    return { start, end, text: this.query.slice(start, end) };
  }

  private buildSuggestions(): Suggestion[] {
    const el = this.queryEditor.nativeElement;
    const range = this.currentTokenRange(el);
    const token = range.text;
    const before = this.query.slice(0, range.start).trim();
    const words = before.length > 0 ? before.split(/\s+/) : [];
    const prev = words.length > 0 ? words[words.length - 1].toLowerCase() : '';
    const lower = token.toLowerCase();

    // Inside a quoted method literal: suggest matching method names.
    if (token.startsWith("'")) {
      const partial = token.slice(1).toLowerCase();
      return this.methodNames
        .filter(m => m.toLowerCase().includes(partial))
        .slice(0, 12)
        .map(m => ({ label: m, insert: `'${m}'`, type: 'method' as const }));
    }

    let candidates: Suggestion[];

    if (prev === 'select' || before === '') {
      candidates = [
        { label: '*', insert: '*', type: 'value' },
        { label: 'COUNT()', insert: 'COUNT(', type: 'aggregate' },
        { label: 'AVG()', insert: 'AVG(', type: 'aggregate' },
        { label: 'MIN()', insert: 'MIN(', type: 'aggregate' },
        { label: 'MAX()', insert: 'MAX(', type: 'aggregate' },
        { label: 'SUM()', insert: 'SUM(', type: 'aggregate' },
        ...this.methodNames.slice(0, 20).map(m => ({ label: m, insert: m, type: 'method' as const }))
      ];
    } else if (prev === 'from') {
      candidates = [{ label: 'telemetry', insert: 'telemetry', type: 'keyword' }];
    } else if (prev === 'where') {
      candidates = [
        { label: 'method', insert: 'method', type: 'column' },
        { label: 'value', insert: 'value', type: 'column' },
        { label: 'timestamp', insert: 'timestamp', type: 'column' },
        { label: 'type', insert: 'type', type: 'column' }
      ];
    } else if (prev === 'method' || prev === '=' || prev.endsWith('(')) {
      candidates = this.methodNames.slice(0, 30).map(m => ({
        label: m, insert: `'${m}'`, type: 'method' as const
      }));
    } else if (prev === 'order') {
      candidates = [{ label: 'BY', insert: 'BY', type: 'keyword' }];
    } else if (prev === 'by') {
      candidates = [
        { label: 'method', insert: 'method', type: 'column' },
        { label: 'value', insert: 'value', type: 'column' },
        { label: 'timestamp', insert: 'timestamp', type: 'column' },
        { label: 'type', insert: 'type', type: 'column' },
        { label: 'ASC', insert: 'ASC', type: 'keyword' },
        { label: 'DESC', insert: 'DESC', type: 'keyword' }
      ];
    } else if (prev === 'limit') {
      candidates = ['10', '50', '100', '500', '1000'].map(n => ({
        label: n, insert: n, type: 'value' as const
      }));
    } else {
      candidates = [
        { label: 'SELECT', insert: 'SELECT', type: 'keyword' },
        { label: 'FROM', insert: 'FROM', type: 'keyword' },
        { label: 'WHERE', insert: 'WHERE', type: 'keyword' },
        { label: 'ORDER BY', insert: 'ORDER BY', type: 'keyword' },
        { label: 'LIMIT', insert: 'LIMIT', type: 'keyword' },
        { label: 'COUNT()', insert: 'COUNT(', type: 'aggregate' },
        { label: 'AVG()', insert: 'AVG(', type: 'aggregate' },
        { label: 'MIN()', insert: 'MIN(', type: 'aggregate' },
        { label: 'MAX()', insert: 'MAX(', type: 'aggregate' },
        { label: 'SUM()', insert: 'SUM(', type: 'aggregate' },
        { label: 'telemetry', insert: 'telemetry', type: 'keyword' },
        { label: 'method', insert: 'method', type: 'column' },
        { label: 'value', insert: 'value', type: 'column' },
        { label: 'timestamp', insert: 'timestamp', type: 'column' },
        { label: 'type', insert: 'type', type: 'column' }
      ];
    }

    return candidates
      .filter(c => c.label.toLowerCase().startsWith(lower))
      .slice(0, 12);
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