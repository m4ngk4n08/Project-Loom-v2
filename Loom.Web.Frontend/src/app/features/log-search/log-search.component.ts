import { Component, DestroyRef, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { LogsService, SearchHit } from '../../core/services/logs.service';

const MAX_RESULTS_OPTIONS = [10, 20, 50, 100];

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

@Component({
  selector: 'app-log-search',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './log-search.component.html',
  styleUrls: ['./log-search.component.scss']
})
export class LogSearchComponent {
  private logsService = inject(LogsService);
  private destroyRef = inject(DestroyRef);

  maxResultsOptions = MAX_RESULTS_OPTIONS;

  queryText = signal('');
  maxResults = signal(20);

  results = signal<SearchHit[]>([]);
  isSearching = signal(false);
  searchError = signal<string | null>(null);
  lastQuery = signal('');
  searchTimeMs = signal<number | null>(null);
  hasSearched = signal(false);

  submit(): void {
    const query = this.queryText();
    if (!isSearchableQuery(query)) return;

    const trimmed = query.trim();
    this.isSearching.set(true);
    this.searchError.set(null);

    this.logsService.search(trimmed, this.maxResults())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.results.set(response.results);
          this.searchTimeMs.set(response.searchTimeMs);
          this.lastQuery.set(trimmed);
          this.hasSearched.set(true);
          this.isSearching.set(false);
        },
        error: (err) => {
          console.error('Log search failed:', err);
          this.lastQuery.set(trimmed);
          this.hasSearched.set(true);
          this.isSearching.set(false);
          this.searchError.set('Search failed. Check the connection and try again.');
        }
      });
  }

  retry(): void {
    this.queryText.set(this.lastQuery());
    this.submit();
  }

  barWidth(score: number): number {
    const top = this.results()[0]?.score ?? 0;
    return scoreBarWidth(score, top);
  }
}
