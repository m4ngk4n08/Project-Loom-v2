import { Component, OnInit, DestroyRef, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { LogsService, LogEntry } from '../../core/services/logs.service';

const MAX_BUFFERED_ENTRIES = 2000;
const BACKFILL_COUNT = 200;

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
  paused = signal(false);
  isConnected = signal(false);

  isLoadingBackfill = signal(false);
  backfillError = signal<string | null>(null);

  filteredEntries = computed(() => {
    const category = this.categoryFilter();
    const entries = this.entries();
    return category === '' ? entries : entries.filter(e => e.category === category);
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
  }

  severityClass(level: string): string {
    return `level-${level.toLowerCase()}`;
  }

  formatTimestamp(timestampUtc: string): string {
    return new Date(timestampUtc).toLocaleTimeString();
  }
}
