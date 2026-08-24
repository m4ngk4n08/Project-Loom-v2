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
}
