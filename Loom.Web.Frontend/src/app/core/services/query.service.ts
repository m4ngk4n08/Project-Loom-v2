import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface QueryRequest {
  query: string;
}

export interface QueryResponse {
  columns: string[];
  rows: QueryRow[];
  executionTimeMs: number;
}

export interface QueryRow {
  values: QueryValue[];
}

/**
 * Mirrors Loom.Web.Contracts.Dtos.QueryValue: a flat record where at most one
 * field is populated. JSON serialization uses WhenWritingNull, so unset fields
 * are omitted rather than sent as null — a SQL NULL cell therefore arrives as
 * an empty object `{}`. Callers must handle the no-key case.
 */
export interface QueryValue {
  text?: string;
  number?: number;
  timestamp?: string;
}

@Injectable({
  providedIn: 'root'
})
export class QueryService {
  private http = inject(HttpClient);

  execute(query: string): Observable<QueryResponse> {
    return this.http.post<QueryResponse>('/api/query', { query } as QueryRequest);
  }
}
