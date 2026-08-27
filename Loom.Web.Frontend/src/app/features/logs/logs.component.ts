import { Component, OnInit, DestroyRef, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { LogsService, LogEntry, LogExportFilters, SearchHit } from '../../core/services/logs.service';

const MAX_BUFFERED_ENTRIES = 2000;
const BACKFILL_COUNT = 200;

const LOG_LEVELS = ['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical'];

// `<input type="datetime-local">` yields a timezone-naive string like "2026-08-24T14:30".
// `new Date(...)` parses that as wall-clock time in the BROWSER'S local timezone (the
// interpretation the user intended), so converting it here - before it ever leaves the
// browser - is the only place the intended instant can be recovered correctly.
export function toUtcIso(localDateTimeValue: string): string | undefined {
  if (!localDateTimeValue) return undefined;
  const parsed = new Date(localDateTimeValue);
  return Number.isNaN(parsed.getTime()) ? undefined : parsed.toISOString();
}

// A search box with an empty/whitespace query is a no-op, not an error - there is
// nothing to search for and no HTTP call should fire.
export function isSearchableQuery(query: string): boolean {
  return query.trim().length > 0;
}

// Bar width is relative to the top result in the CURRENT set, not an absolute
// percentage - BM25 scores are unbounded. Guard the zero-corpus/zero-score case so
// division never produces NaN or Infinity.
export function scoreBarWidth(score: number, topScore: number): number {
  if (!Number.isFinite(topScore) || topScore <= 0) return 0;
  const pct = (score / topScore) * 100;
  if (!Number.isFinite(pct)) return 0;
  return Math.max(0, Math.min(100, pct));
}

// A 32-hex trace id is unreadable inline and destroys the row layout. Eight
// chars is enough to eyeball-match two lines from the same trace; the full id
// stays available via the title attribute and click-to-filter.
export function shortTraceId(traceId: string | undefined): string | undefined {
  if (!traceId) return undefined;
  return traceId.slice(0, 8);
}

// An empty filter matches everything, including rows with no trace id at all.
// A non-empty filter must NOT match a row that has no trace id - "show me this
// trace" and "show me everything untraced" are different requests.
export function matchesTraceFilter(entryTraceId: string | undefined, filter: string): boolean {
  if (filter === '') return true;
  if (entryTraceId === undefined) return false;
  return entryTraceId === filter;
}

interface DisplayRow {
  timestampUtc: string;
  level: string;
  category: string;
  message: string;
  exceptionType?: string;
  exceptionMessage?: string;
  score: number | null;
  traceId?: string;
}

@Component({
  selector: 'app-logs',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './logs.component.html',
  styleUrls: ['./logs.component.scss']
})
export class LogsComponent implements OnInit {
  private logsService = inject(LogsService);
  private destroyRef = inject(DestroyRef);

  private buffer: LogEntry[] = [];

  entries = signal<LogEntry[]>([]);
  categories = signal<string[]>([]);
  categoryFilter = signal<string>('');
  traceFilter = signal<string>('');
  paused = signal(false);
  isConnected = signal(false);

  isLoadingBackfill = signal(false);
  backfillError = signal<string | null>(null);

  logLevels = LOG_LEVELS;
  showExportModal = signal(false);
  exportFormat: LogExportFilters['format'] = 'json';
  exportCategory = '';
  exportMinLevel = '';
  exportFrom = '';
  exportTo = '';
  exportLimit = 1000;

  searchQuery = signal('');
  searchResults = signal<SearchHit[] | null>(null);
  isSearching = signal(false);
  searchError = signal<string | null>(null);
  searchTimeMs = signal<number | null>(null);
  lastQuery = signal('');

  filteredEntries = computed(() => {
    const category = this.categoryFilter();
    const trace = this.traceFilter();
    const entries = this.entries();
    return entries.filter(e =>
      (category === '' || e.category === category) &&
      matchesTraceFilter(e.traceId, trace)
    );
  });

  // Category filter applies to search results too, rather than being disabled in
  // search mode - a SearchHit already carries its category as `source`, filtering
  // needs no extra round trip, and the control stays meaningfully interactive
  // instead of silently going inert the moment a search is active.
  filteredSearchResults = computed(() => {
    const results = this.searchResults();
    if (results === null) return null;
    const category = this.categoryFilter();
    return category === '' ? results : results.filter(r => r.source === category);
  });

  displayRows = computed<DisplayRow[]>(() => {
    const results = this.filteredSearchResults();
    if (results !== null) {
      return results.map(r => ({
        timestampUtc: r.timestamp,
        level: r.level,
        category: r.source,
        message: r.content,
        exceptionType: r.exceptionType,
        exceptionMessage: r.exceptionMessage,
        score: r.score
        // SearchHit carries no trace id, so search rows show no chip. Widening the
        // BM25 search DTO is a separate backend change.
      }));
    }
    return this.filteredEntries().map(e => ({
      timestampUtc: e.timestampUtc,
      level: e.level,
      category: e.category,
      message: e.message,
      exceptionType: e.exceptionType,
      exceptionMessage: e.exceptionMessage,
      score: null,
      traceId: e.traceId
    }));
  });

  ngOnInit(): void {
    this.loadCategories();
    this.loadBackfill();
    this.connectLive();
  }

  private loadCategories(): void {
    this.logsService.getCategories().subscribe({
      next: (categories) => this.categories.set(categories),
      error: (err) => console.error('Failed to load log categories:', err)
    });
  }

  loadBackfill(): void {
    this.isLoadingBackfill.set(true);
    this.backfillError.set(null);

    this.logsService.getRecent(BACKFILL_COUNT).subscribe({
      next: (entries) => {
        // /api/logs returns newest-first; the live view appends chronologically.
        this.buffer = [...entries].reverse();
        this.entries.set([...this.buffer]);
        this.isLoadingBackfill.set(false);
      },
      error: (err) => {
        console.error('Failed to load log backfill:', err);
        this.isLoadingBackfill.set(false);
        this.backfillError.set('Could not load recent logs.');
      }
    });
  }

  private connectLive(): void {
    this.logsService.connectLive()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (entry: LogEntry) => {
          this.isConnected.set(true);
          if (this.paused()) return;
          this.appendEntry(entry);
        },
        error: () => this.isConnected.set(false),
        complete: () => this.isConnected.set(false)
      });
  }

  private appendEntry(entry: LogEntry): void {
    this.buffer.push(entry);
    if (this.buffer.length > MAX_BUFFERED_ENTRIES) {
      this.buffer.splice(0, this.buffer.length - MAX_BUFFERED_ENTRIES);
    }
    this.entries.set([...this.buffer]);
  }

  togglePause(): void {
    this.paused.update(v => !v);
  }

  clear(): void {
    this.buffer = [];
    this.entries.set([]);
    this.traceFilter.set('');
  }

  severityClass(level: string): string {
    return `level-${level.toLowerCase()}`;
  }

  formatTimestamp(timestampUtc: string): string {
    return new Date(timestampUtc).toLocaleTimeString();
  }

  get liveEmptyMessage(): string {
    if (this.traceFilter()) return 'No log entries for this trace';
    if (this.categoryFilter()) return 'No log entries for this category';
    return 'No log entries';
  }

  shortTrace(traceId: string | undefined): string | undefined {
    return shortTraceId(traceId);
  }

  filterByTrace(traceId: string | undefined): void {
    if (!traceId) return;
    this.traceFilter.set(traceId);
  }

  clearTraceFilter(): void {
    this.traceFilter.set('');
  }

  runSearch(): void {
    const query = this.searchQuery();
    if (!isSearchableQuery(query)) {
      this.clearSearch();
      return;
    }

    const trimmed = query.trim();
    this.isSearching.set(true);
    this.searchError.set(null);
    // The trace filter does not apply to search results (SearchHit has no
    // trace id), so leaving it set would show an active filter chip that is
    // filtering nothing.
    this.traceFilter.set('');

    this.logsService.search(trimmed)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.searchResults.set(response.results);
          this.searchTimeMs.set(response.searchTimeMs);
          this.lastQuery.set(trimmed);
          this.isSearching.set(false);
        },
        error: (err) => {
          console.error('Log search failed:', err);
          this.lastQuery.set(trimmed);
          this.isSearching.set(false);
          this.searchError.set('Search failed. Check the connection and try again.');
          // First-ever search failing still needs to flip out of live mode so the
          // error state renders; a retry after prior success leaves existing
          // results alone since the error branch is checked before the empty check.
          if (this.searchResults() === null) {
            this.searchResults.set([]);
          }
        }
      });
  }

  clearSearch(): void {
    this.searchQuery.set('');
    this.searchResults.set(null);
    this.searchError.set(null);
    this.searchTimeMs.set(null);
    this.lastQuery.set('');
  }

  barWidth(score: number): number {
    const top = this.filteredSearchResults()?.[0]?.score ?? 0;
    return scoreBarWidth(score, top);
  }

  openExportModal(): void {
    this.exportCategory = this.categoryFilter();
    this.showExportModal.set(true);
  }

  closeExportModal(): void {
    this.showExportModal.set(false);
  }

  runExport(): void {
    const url = this.logsService.buildExportUrl({
      format: this.exportFormat,
      category: this.exportCategory || undefined,
      minLevel: this.exportMinLevel || undefined,
      from: toUtcIso(this.exportFrom),
      to: toUtcIso(this.exportTo),
      limit: this.exportLimit || undefined
    });

    // Real anchor-click navigation (not window.location, not target="_blank") so the
    // browser downloads via Content-Disposition without leaving or opening a tab.
    const anchor = document.createElement('a');
    anchor.href = url;
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);

    this.closeExportModal();
  }
}
