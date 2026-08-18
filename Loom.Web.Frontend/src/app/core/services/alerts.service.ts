import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface AlertRule {
  name: string;
  metricName: string;
  window: string;
  condition: string;
  threshold: number;
  actions: string[];
}

@Injectable({
  providedIn: 'root'
})
export class AlertsService {
  private http = inject(HttpClient);

  getAlerts(): Observable<AlertRule[]> {
    return this.http.get<AlertRule[]>('/api/alerts');
  }

  testAlert(alertName: string): Observable<void> {
    return this.http.post<void>(`/api/alerts/${encodeURIComponent(alertName)}/test`, null);
  }

  silenceAlert(alertName: string, duration: string): Observable<void> {
    const params = new HttpParams().set('duration', duration);
    return this.http.put<void>(`/api/alerts/${encodeURIComponent(alertName)}/silence`, null, { params });
  }
}
