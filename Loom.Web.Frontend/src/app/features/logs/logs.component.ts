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

export interface TemplateGroup {
  template: string;
  count: number;
  level: string;
  category: string;
  latestTimestampUtc: string;
  sample: string;
}

// LOG_LEVELS is ordered Trace -> Critical, so its index IS the severity rank.
// An unrecognised level ranks below every known one rather than throwing: a
// future server-side level must not be able to hijack a group's severity.
export function levelRank(level: string): number {
  return LOG_LEVELS.indexOf(level);
}

// Two lines with different argument values but the same {OriginalFormat} are
// the same EVENT. Grouping on the template is what commit 1 bought by storing
// it separately from the rendered Message.
//
// Entries with no template are EXCLUDED from the groups and counted instead.
// Collapsing them into one bucket would claim they are the same event when
// nothing establishes that; giving each its own group shreds the table when
// messages carry varying ids. The count is the honest third option - it makes
// grouping's coverage gap visible rather than hiding it.
export function groupByTemplate(
  entries: LogEntry[]
): { groups: TemplateGroup[]; ungroupedCount: number } {
  const byTemplate = new Map<string, LogEntry[]>();
  let ungroupedCount = 0;

  for (const entry of entries) {
    if (!entry.template) {
      ungroupedCount++;
      continue;
    }
    const list = byTemplate.get(entry.template);
    if (list) {
      list.push(entry);
    } else {
      byTemplate.set(entry.template, [entry]);
    }
  }

  const groups: TemplateGroup[] = [];
  for (const [template, groupEntries] of byTemplate) {
    let latest = groupEntries[0];
    let bestLevel = groupEntries[0];
    let allSameCategory = true;

    for (const entry of groupEntries) {
      if (Date.parse(entry.timestampUtc) > Date.parse(latest.timestampUtc)) {
        latest = entry;
      }
      if (levelRank(entry.level) > levelRank(bestLevel.level)) {
        bestLevel = entry;
      }
      if (entry.category !== groupEntries[0].category) {
        allSameCategory = false;
      }
    }

    groups.push({
      template,
      count: groupEntries.length,
      level: bestLevel.level,
      category: allSameCategory ? groupEntries[0].category : 'multiple',
      latestTimestampUtc: latest.timestampUtc,
      sample: latest.message
    });
  }

  groups.sort((a, b) => {
    if (b.count !== a.count) return b.count - a.count;
    return Date.parse(b.latestTimestampUtc) - Date.parse(a.latestTimestampUtc);
  });

  return { groups, ungroupedCount };
}

export interface DisplayRow {
  timestampUtc: string;
  level: string;
  category: string;
  message: string;
  eventId: number;
  exceptionType?: string;
  exceptionMessage?: string;
  score: number | null;
  traceId?: string;
  template?: string;
  argumentsJson?: string;
  spanId?: string;
}

export interface LogArgument {
  name: string;
  value: string;
}

// ArgumentsJson can hold text that is NOT valid JSON: the backend preserves a
// malformed payload verbatim rather than dropping it, so bad input reaches the
// browser by design. Returning [] on a parse failure keeps one bad log line
// from taking down the whole view - an uncaught throw here renders inside the
// row loop and blanks the page.
export function parseArguments(argumentsJson: string | undefined): LogArgument[] {
  if (!argumentsJson) return [];
  let parsed: unknown;
  try {
    parsed = JSON.parse(argumentsJson);
  } catch {
    return [];
  }
  if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) return [];
  return Object.entries(parsed as Record<string, unknown>).map(([name, value]) => ({
    name,
    value: typeof value === 'string' ? value : JSON.stringify(value)
  }));
}

// Expansion state cannot key on $index: the live buffer shifts when
// appendEntry trims past MAX_BUFFERED_ENTRIES, so an index would silently
// point at a different row. It also cannot BECOME the @for track expression -
// Angular throws NG0955 on duplicate track keys, and two identical lines at
// the same timestamp is ordinary. So track stays on $index and this key drives
// expansion only; a collision means two identical rows expand together, which
// is cosmetic rather than a crash.
export function rowKey(row: DisplayRow): string {
  return `${row.timestampUtc}#${row.level}#${row.message}`;
}

// Level is a filter, not a search term. BM25 indexes LogRecord.Message and
// nothing else, so typing "warning" into the search box can never match a
// severity - and indexing it would be wrong anyway: IDF collapses over a
// six-value field, and the word "error" in a message body would conflate with
// the Error level.
//
// An unrecognised entry level always passes. A filter should never hide a
// record it cannot classify - silently dropping data is worse than showing a
// row the user did not ask for.
export function meetsMinLevel(level: string, minLevel: string): boolean {
  if (minLevel === '') return true;
  const entryRank = levelRank(level);
  if (entryRank < 0) return true;
  return entryRank >= levelRank(minLevel);
}

// A row is clickable but contains its own controls (the trace chip). Guarding
// on `target === currentTarget` would be wrong: the row also contains plain
// spans, so clicking the message text would stop working. Using
// Element.closest() alone would also be wrong: it walks past currentTarget to
// the document, so an interactive ancestor ABOVE the row would falsely
// suppress. Walk up and stop at currentTarget.
export function isInteractiveEventTarget(
  target: EventTarget | null,
  currentTarget: EventTarget | null
): boolean {
  if (!(target instanceof Element) || !(currentTarget instanceof Element)) return false;
  const interactiveTags = new Set(['button', 'a', 'input', 'select', 'textarea']);
  let el: Element | null = target;
  while (el && el !== currentTarget) {
    if (interactiveTags.has(el.tagName.toLowerCase())) return true;
    el = el.parentElement;
  }
  return false;
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
  levelFilter = signal<string>('');
  // A view preference, not a filter - deliberately sticky across searches
  // and buffer clears, unlike traceFilter.
  grouped = signal(false);
  expandedKey = signal<string | null>(null);
  // TemplateGroup has no natural id field of its own, but groupByTemplate already
  // keys its internal map on the template string, so it's unique across a single
  // grouping pass - no rowKey-style hashing needed here.
  expandedGroupTemplate = signal<string | null>(null);
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
    const level = this.levelFilter();
    const entries = this.entries();
    return entries.filter(e =>
      (category === '' || e.category === category) &&
      matchesTraceFilter(e.traceId, trace) &&
      meetsMinLevel(e.level, level)
    );
  });

  templateGroups = computed(() => groupByTemplate(this.filteredEntries()));

  // Category filter applies to search results too, rather than being disabled in
  // search mode - a SearchHit already carries its category as `source`, filtering
  // needs no extra round trip, and the control stays meaningfully interactive
  // instead of silently going inert the moment a search is active.
  filteredSearchResults = computed(() => {
    const results = this.searchResults();
    if (results === null) return null;
    const category = this.categoryFilter();
    const level = this.levelFilter();
    return results.filter(r =>
      (category === '' || r.source === category) &&
      meetsMinLevel(r.level, level)
    );
  });

  displayRows = computed<DisplayRow[]>(() => {
    const results = this.filteredSearchResults();
    if (results !== null) {
      return results.map(r => ({
        timestampUtc: r.timestamp,
        level: r.level,
        category: r.source,
        message: r.content,
        eventId: r.eventId,
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
      eventId: e.eventId,
      exceptionType: e.exceptionType,
      exceptionMessage: e.exceptionMessage,
      score: null,
      traceId: e.traceId,
      template: e.template,
      argumentsJson: e.argumentsJson,
      spanId: e.spanId
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
    this.expandedKey.set(null);
    this.expandedGroupTemplate.set(null);
  }

  severityClass(level: string): string {
    return `level-${level.toLowerCase()}`;
  }

  formatTimestamp(timestampUtc: string): string {
    return new Date(timestampUtc).toLocaleTimeString();
  }

  get liveEmptyMessage(): string {
    if (this.traceFilter()) return 'No log entries for this trace';
    if (this.levelFilter()) return 'No log entries at this level or above';
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

  toggleGrouped(): void {
    this.grouped.update(v => !v);
  }

  isExpanded(row: DisplayRow): boolean {
    return this.expandedKey() === rowKey(row);
  }

  toggleExpanded(row: DisplayRow): void {
    const key = rowKey(row);
    this.expandedKey.set(this.expandedKey() === key ? null : key);
  }

  rowArguments(row: DisplayRow): LogArgument[] {
    return parseArguments(row.argumentsJson);
  }

  isGroupExpanded(group: TemplateGroup): boolean {
    return this.expandedGroupTemplate() === group.template;
  }

  toggleGroupExpanded(group: TemplateGroup): void {
    this.expandedGroupTemplate.set(this.expandedGroupTemplate() === group.template ? null : group.template);
  }

  onRowActivate(row: DisplayRow, event: Event): void {
    if (isInteractiveEventTarget(event.target, event.currentTarget)) return;
    // Space would otherwise scroll the page - preventDefault has to happen here
    // rather than inline in the template, above where it would suppress the
    // trace chip's own activation on the keydown path.
    if (event.type === 'keydown') event.preventDefault();
    this.toggleExpanded(row);
  }

  onGroupActivate(group: TemplateGroup, event: Event): void {
    // Same guard as onRowActivate. Group rows have no nested control today, but
    // the two views must not drift into different activation rules.
    if (isInteractiveEventTarget(event.target, event.currentTarget)) return;
    if (event.type === 'keydown') event.preventDefault();
    this.toggleGroupExpanded(group);
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
