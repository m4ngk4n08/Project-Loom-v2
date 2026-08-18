import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { QueryResponse, QueryService } from './query.service';

@Injectable({
  providedIn: 'root'
})
export class MetricsExplorerService {
  private http = inject(HttpClient);
  private queryService = inject(QueryService);

  getMetricNames(): Observable<string[]> {
    return this.http.get<string[]>('/api/exporters/metrics/names');
  }

  getMetricData(metricName: string, lookback: string = '1h'): Observable<QueryResponse> {
    const query = `SELECT * FROM metrics WHERE name = "${metricName}" AND timestamp > NOW() - ${lookback}`;
    return this.queryService.execute(query);
  }
}
