import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';

export interface TokenResponse {
  token: string;
  expiresIn: number;
}

export const TOKEN_STORAGE_KEY = 'loom.token';

/** Refresh this far before the token actually expires. 10 minutes on a 60-minute token
 *  puts the timer at ~50 minutes, per methodology 14.7.1. */
const REFRESH_LEAD_SECONDS = 600;

/** Pure. Milliseconds until the refresh timer should fire, given the server's expiresIn.
 *  Floored at 30s so a short or nonsense lifetime cannot produce a tight retry loop. */
export function refreshDelayMs(expiresInSeconds: number): number {
  return Math.max(30_000, (expiresInSeconds - REFRESH_LEAD_SECONDS) * 1000);
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  // sessionStorage, not memory: in-memory-only forces a re-login on every page refresh,
  // which operators work around by writing the token somewhere worse. This IS readable
  // by injected script - acceptable only because the dashboard serves its own embedded
  // bundle and no user-generated HTML. See methodology 14.7.1.
  private readonly tokenSignal = signal<string | null>(readStoredToken());
  private refreshHandle: ReturnType<typeof setTimeout> | null = null;

  readonly token = this.tokenSignal.asReadonly();
  readonly isAuthenticated = computed(() => this.tokenSignal() !== null);

  login(username: string, password: string): Observable<TokenResponse> {
    return this.http.post<TokenResponse>('/api/token', { username, password })
      .pipe(tap(response => this.accept(response)));
  }

  /** Clears the session and sends the browser to the login screen. Safe to call twice. */
  logout(returnUrl?: string): void {
    this.cancelRefresh();
    this.tokenSignal.set(null);
    try { sessionStorage.removeItem(TOKEN_STORAGE_KEY); } catch { /* private mode */ }
    void this.router.navigate(['/login'], returnUrl ? { queryParams: { returnUrl } } : {});
  }

  private accept(response: TokenResponse): void {
    this.tokenSignal.set(response.token);
    try { sessionStorage.setItem(TOKEN_STORAGE_KEY, response.token); } catch { /* private mode */ }
    this.scheduleRefresh(response.expiresIn);
  }

  private scheduleRefresh(expiresInSeconds: number): void {
    this.cancelRefresh();
    this.refreshHandle = setTimeout(() => {
      // The interceptor attaches the current token. A 401 here means the 12-hour
      // absolute cap was reached: the session is over, not renewable.
      this.http.post<TokenResponse>('/api/token/refresh', null).subscribe({
        next: response => this.accept(response),
        error: () => this.logout()
      });
    }, refreshDelayMs(expiresInSeconds));
  }

  private cancelRefresh(): void {
    if (this.refreshHandle !== null) {
      clearTimeout(this.refreshHandle);
      this.refreshHandle = null;
    }
  }
}

function readStoredToken(): string | null {
  try { return sessionStorage.getItem(TOKEN_STORAGE_KEY); } catch { return null; }
}
