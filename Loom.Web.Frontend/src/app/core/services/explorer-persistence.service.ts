import { Injectable, signal } from '@angular/core';

const STORAGE_KEY = 'loom.explorerState';

export interface ExplorerViewState {
  selectedMetric: string | null;
  searchText: string;
  typeFilter: string;
  sortField: 'name' | 'sampleCount' | 'latestValue';
  sortDir: 'asc' | 'desc';
  isListCollapsed: boolean;
  lookback: string;
  compareMetrics: string[];
}

const DEFAULT_STATE: ExplorerViewState = {
  selectedMetric: null,
  searchText: '',
  typeFilter: '',
  sortField: 'name',
  sortDir: 'asc',
  isListCollapsed: false,
  lookback: '1h',
  compareMetrics: []
};

/** Persists Metrics Explorer view state (selection, filters, sort, layout) to
 *  localStorage so a refresh or revisit restores the exact view. */
@Injectable({
  providedIn: 'root'
})
export class ExplorerPersistenceService {
  readonly state = signal<ExplorerViewState>(this.load());

  save(state: ExplorerViewState): void {
    this.state.set(state);
    localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
  }

  reset(): void {
    this.state.set(DEFAULT_STATE);
    localStorage.removeItem(STORAGE_KEY);
  }

  private load(): ExplorerViewState {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return { ...DEFAULT_STATE };
      const parsed = JSON.parse(raw);
      if (typeof parsed !== 'object' || parsed === null) return { ...DEFAULT_STATE };
      return {
        selectedMetric: typeof parsed.selectedMetric === 'string' ? parsed.selectedMetric : null,
        searchText: typeof parsed.searchText === 'string' ? parsed.searchText : '',
        typeFilter: typeof parsed.typeFilter === 'string' ? parsed.typeFilter : '',
        sortField: ['name', 'sampleCount', 'latestValue'].includes(parsed.sortField) ? parsed.sortField : 'name',
        sortDir: ['asc', 'desc'].includes(parsed.sortDir) ? parsed.sortDir : 'asc',
        isListCollapsed: typeof parsed.isListCollapsed === 'boolean' ? parsed.isListCollapsed : false,
        lookback: typeof parsed.lookback === 'string' ? parsed.lookback : '1h',
        compareMetrics: Array.isArray(parsed.compareMetrics)
          ? parsed.compareMetrics.filter((m: unknown) => typeof m === 'string')
          : []
      };
    } catch {
      return { ...DEFAULT_STATE };
    }
  }
}
