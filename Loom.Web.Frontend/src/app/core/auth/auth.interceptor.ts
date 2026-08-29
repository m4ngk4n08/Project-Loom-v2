import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/** Pure. True if this request URL should carry the bearer token.
 *  Relative same-origin API paths only - never an absolute URL, which would leak the
 *  credential to whatever host someone put in it. `/api/token` is excluded because the
 *  login post must not carry a stale token; `/api/token/refresh` is NOT excluded,
 *  because refresh authenticates with the token it is replacing. */
export function shouldAttachToken(url: string): boolean {
  if (url === '/api/token' || url.startsWith('/api/token?')) return false;
  return url.startsWith('/api/') || url.startsWith('/prometheus');
}

export const loomAuthInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const token = auth.token();

  const outbound = token && shouldAttachToken(req.url)
    ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : req;

  return next(outbound).pipe(
    catchError((error: unknown) => {
      // No retry, no refresh-and-replay queue: after a successful login a 401 means the
      // session is genuinely finished, and a retry loop would just hammer the throttle.
      if (error instanceof HttpErrorResponse && error.status === 401 && req.url !== '/api/token') {
        auth.logout(location.pathname + location.search);
      }
      return throwError(() => error);
    })
  );
};
