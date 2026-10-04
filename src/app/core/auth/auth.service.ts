import { Injectable, computed, inject, signal } from '@angular/core';
import {
  ApiError,
  api,
  setApiAuthToken,
  setUnauthorizedHandler,
  unwrap,
  type User,
} from '../api/api';

const TOKEN_KEY = 'comic-studio.token';
const USER_KEY = 'comic-studio.user';

/**
 * Session state. The token lives in localStorage and is mirrored into the API
 * client, which attaches it to every request.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly token = signal<string | null>(null);
  private readonly currentUser = signal<User | null>(null);
  private readonly restorePromise: Promise<void>;

  readonly user = this.currentUser.asReadonly();
  readonly isAuthenticated = computed(() => this.currentUser() !== null);
  readonly tier = computed(() => this.currentUser()?.novelAiTier ?? 'Unknown');

  constructor() {
    const storedToken = localStorage.getItem(TOKEN_KEY);
    if (storedToken) {
      this.token.set(storedToken);
      setApiAuthToken(storedToken);
      const cached = this.readCachedUser();
      if (cached) {
        this.currentUser.set(cached);
      }
    }

    setUnauthorizedHandler(() => this.clearSession());
    this.restorePromise = this.restore();
  }

  /** Resolves once the stored session has been validated against the API. */
  whenReady(): Promise<void> {
    return this.restorePromise;
  }

  /** Used by the SignalR client, which authenticates via query string. */
  getAccessToken(): string | null {
    return this.token();
  }

  async login(email: string, password: string): Promise<void> {
    const auth = await unwrap(
      api.POST('/api/auth/login', { body: { email, password } }),
    );
    this.applySession(auth.accessToken, auth.user);
  }

  async register(email: string, password: string, displayName?: string): Promise<void> {
    const auth = await unwrap(
      api.POST('/api/auth/register', {
        body: { email, password, displayName: displayName?.trim() || null },
      }),
    );
    this.applySession(auth.accessToken, auth.user);
  }

  logout(): void {
    this.clearSession();
  }

  async refreshUser(): Promise<void> {
    if (!this.token()) {
      return;
    }

    try {
      const user = await unwrap(api.GET('/api/auth/me'));
      this.currentUser.set(user);
      localStorage.setItem(USER_KEY, JSON.stringify(user));
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        this.clearSession();
      }
    }
  }

  private async restore(): Promise<void> {
    if (!this.token()) {
      return;
    }
    await this.refreshUser();
  }

  private applySession(token: string, user: User): void {
    this.token.set(token);
    setApiAuthToken(token);
    localStorage.setItem(TOKEN_KEY, token);
    this.currentUser.set(user);
    localStorage.setItem(USER_KEY, JSON.stringify(user));
  }

  private clearSession(): void {
    this.token.set(null);
    this.currentUser.set(null);
    setApiAuthToken(null);
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(USER_KEY);
  }

  private readCachedUser(): User | null {
    const raw = localStorage.getItem(USER_KEY);
    if (!raw) {
      return null;
    }

    try {
      return JSON.parse(raw) as User;
    } catch {
      return null;
    }
  }
}
