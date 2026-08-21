import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { QueryResponse, QueryService } from './query.service';

export interface MetricSummary {
  name: string;
  type: string;
  unit: string;
  sampleCount: number;
  latestValue: number;
  average: number;
  min: number;
  max: number;
  p95: number;
  firstTimestampUtc: string;
  lastTimestampUtc: string;
}

@Injectable({
  providedIn: 'root'
})
export class MetricsExplorerService {
  private http = inject(HttpClient);
  private queryService = inject(QueryService);

  getMetricNames(): Observable<string[]> {
    return this.http.get<string[]>('/api/exporters/metrics/names');
  }

  getMetricSummary(): Observable<MetricSummary[]> {
    return this.http.get<MetricSummary[]>('/api/exporters/metrics/summary');
  }

  getMetricData(metricName: string, lookback: string = '1h'): Observable<QueryResponse> {
    const query = `SELECT * FROM telemetry WHERE method = '${metricName}' LIMIT 500`;
    return this.queryService.execute(query);
  }
}
