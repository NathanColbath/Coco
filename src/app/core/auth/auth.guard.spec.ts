import { TestBed } from '@angular/core/testing';
import { UrlTree, provideRouter } from '@angular/router';
import { authGuard, guestGuard } from './auth.guard';
import { AuthService } from './auth.service';

/**
 * Guards are async and Angular calls them inside an injection context. These
 * tests exist because `inject()` after an `await` throws NG0203 instead of
 * redirecting, which is a silent-failure class of bug: the app builds, the
 * tests pass, and only a real page load breaks.
 */
function authStub(authenticated: boolean) {
  return {
    whenReady: async () => undefined,
    isAuthenticated: () => authenticated,
  } as unknown as AuthService;
}

async function run(guard: typeof authGuard, authenticated: boolean) {
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: AuthService, useValue: authStub(authenticated) }],
  });

  return TestBed.runInInjectionContext(() => guard(null as never, null as never));
}

describe('authGuard', () => {
  it('admits a signed-in user', async () => {
    await expect(run(authGuard, true)).resolves.toBe(true);
  });

  it('redirects a signed-out user to the login page instead of throwing', async () => {
    const result = await run(authGuard, false);

    expect(result).toBeInstanceOf(UrlTree);
    expect(String(result)).toBe('/login');
  });
});

describe('guestGuard', () => {
  it('admits a signed-out visitor', async () => {
    await expect(run(guestGuard, false)).resolves.toBe(true);
  });

  it('sends a signed-in user to the dashboard', async () => {
    const result = await run(guestGuard, true);

    expect(result).toBeInstanceOf(UrlTree);
    expect(String(result)).toBe('/dashboard');
  });
});
