import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface ExporterStatus {
  name: string;
  isHealthy: boolean;
  lastSuccessUtc: string | null;
  lastFailureUtc: string | null;
  lastError: string | null;
  totalExports: number;
  totalFailures: number;
}

@Injectable({
  providedIn: 'root'
})
export class ExportersService {
  private http = inject(HttpClient);

  getStatus(): Observable<ExporterStatus[]> {
    return this.http.get<ExporterStatus[]>('/api/exporters/status');
  }
}
