import { Injectable, signal } from '@angular/core';

const STORAGE_KEY = 'loom.queryHistory';
const MAX_HISTORY = 20;

/** Persists executed LoomQL queries to localStorage so users can recall them
 *  across sessions with Ctrl+ArrowUp/Down. Dedupes and caps the list. */
@Injectable({
  providedIn: 'root'
})
export class QueryHistoryService {
  readonly history = signal<string[]>(this.load());

  add(query: string): void {
    const trimmed = query.trim();
    if (!trimmed) return;

    const updated = [
      trimmed,
      ...this.history().filter(h => h.toLowerCase() !== trimmed.toLowerCase())
    ].slice(0, MAX_HISTORY);

    this.history.set(updated);
    localStorage.setItem(STORAGE_KEY, JSON.stringify(updated));
  }

  clear(): void {
    this.history.set([]);
    localStorage.removeItem(STORAGE_KEY);
  }

  private load(): string[] {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return [];
      const parsed = JSON.parse(raw);
      return Array.isArray(parsed)
        ? parsed.filter(h => typeof h === 'string').slice(0, MAX_HISTORY)
        : [];
    } catch {
      return [];
    }
  }
}