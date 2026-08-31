import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) return true;

  // Redirect rather than allowing the shell to render and 401 every panel at once.
  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};
