import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { WebSocketService } from './websocket.service';

export interface LogEntry {
  message: string;
  category: string;
  level: string;
  timestampUtc: string;
  eventId: number;
  exceptionType?: string;
  exceptionMessage?: string;
  template?: string;
  argumentsJson?: string;
  traceId?: string;
  spanId?: string;
}

export interface LogExportFilters {
  format: 'json' | 'csv' | 'text';
  category?: string;
  minLevel?: string;
  from?: string;
  to?: string;
  limit?: number;
}

export interface SearchHit {
  content: string;
  score: number;
  timestamp: string;
  source: string;
  level: string;
  eventId: number;
  exceptionType?: string;
  exceptionMessage?: string;
  template?: string;
  argumentsJson?: string;
  traceId?: string;
  spanId?: string;
}

export interface LogSearchResponse {
  query: string;
  totalResults: number;
  searchTimeMs: number;
  results: SearchHit[];
}

export interface ExplainRequest {
  template: string;
  argumentsJson?: string;
  category?: string;
  level?: string;
  exceptionType?: string;
}

export interface ExplainResponse {
  explanation: string;
  modelUsed: string;
  sentText: string;
  inputTokens: number;
  outputTokens: number;
}

@Injectable({
  providedIn: 'root'
})
export class LogsService {
  private http = inject(HttpClient);
  private wsService = inject(WebSocketService);

  getRecent(count = 100, category?: string): Observable<LogEntry[]> {
    const params: Record<string, string> = { count: String(count) };
    if (category) params['category'] = category;
    return this.http.get<LogEntry[]>('/api/logs', { params });
  }

  getCategories(): Observable<string[]> {
    return this.http.get<string[]>('/api/logs/categories');
  }

  connectLive(): Observable<LogEntry> {
    return this.wsService.connect('/ws/logs');
  }

  // Returns the URL only - the caller must navigate to it via an anchor click (not
  // HttpClient) so the server's Content-Disposition: attachment header is honored.
  buildExportUrl(filters: LogExportFilters): string {
    const params: string[] = [`format=${encodeURIComponent(filters.format)}`];
    if (filters.category) params.push(`category=${encodeURIComponent(filters.category)}`);
    if (filters.minLevel) params.push(`minLevel=${encodeURIComponent(filters.minLevel)}`);
    if (filters.from) params.push(`from=${encodeURIComponent(filters.from)}`);
    if (filters.to) params.push(`to=${encodeURIComponent(filters.to)}`);
    if (filters.limit) params.push(`limit=${encodeURIComponent(String(filters.limit))}`);
    return `/api/logs/export?${params.join('&')}`;
  }

  search(query: string, maxResults = 20): Observable<LogSearchResponse> {
    return this.http.post<LogSearchResponse>('/api/logs/search', { query, maxResults });
  }

  explain(request: ExplainRequest): Observable<ExplainResponse> {
    return this.http.post<ExplainResponse>('/api/logs/explain', request);
  }
}
