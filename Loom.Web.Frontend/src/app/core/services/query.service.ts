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

export type QueryValue =
  | { text: string }
  | { number: number }
  | { boolean: boolean }
  | { timestamp: string }
  | { null: null };

@Injectable({
  providedIn: 'root'
})
export class QueryService {
  private http = inject(HttpClient);

  execute(query: string): Observable<QueryResponse> {
    return this.http.post<QueryResponse>('/api/query', { query } as QueryRequest);
  }
}
