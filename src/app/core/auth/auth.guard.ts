import { inject } from '@angular/core';
import { type CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Both guards resolve their dependencies before the first `await`.
 *
 * `inject()` only works synchronously: once a guard suspends, the injection
 * context is gone and any later `inject()` throws NG0203. Capturing everything
 * up front keeps the redirect working.
 */
export const authGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  await auth.whenReady();

  return auth.isAuthenticated() ? true : router.createUrlTree(['/login']);
};

export const guestGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  await auth.whenReady();

  return auth.isAuthenticated() ? router.createUrlTree(['/dashboard']) : true;
};
