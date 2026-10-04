import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatMenuModule } from '@angular/material/menu';
import { MatDividerModule } from '@angular/material/divider';
import { ThemeService } from '../theme/theme.service';
import { AuthService } from '../auth/auth.service';
import { StatusChip } from '../../shared/ui';
import { tierLabel } from '../models/model-registry';

interface NavItem {
  path: string;
  label: string;
  icon: string;
}

@Component({
  selector: 'cs-app-shell',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatToolbarModule,
    MatSidenavModule,
    MatListModule,
    MatIconModule,
    MatButtonModule,
    MatTooltipModule,
    MatMenuModule,
    MatDividerModule,
    StatusChip,
  ],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss',
})
export class AppShell {
  protected readonly theme = inject(ThemeService);
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly collapsed = signal(false);

  protected readonly navItems: NavItem[] = [
    { path: '/dashboard', label: 'Dashboard', icon: 'space_dashboard' },
    { path: '/stories', label: 'Stories', icon: 'menu_book' },
    { path: '/projects', label: 'Comics', icon: 'auto_stories' },
    { path: '/settings', label: 'Settings', icon: 'settings' },
  ];

  protected readonly initial = computed(() => {
    const name = this.auth.user()?.displayName ?? this.auth.user()?.email ?? '?';
    return name.charAt(0).toUpperCase();
  });

  protected readonly novelAiChip = computed(() => {
    const user = this.auth.user();
    if (!user?.novelAiConnected) {
      return { label: 'NovelAI not connected', tone: 'neutral' as const, icon: 'link_off' };
    }
    return {
      label: `NovelAI · ${tierLabel(user.novelAiTier)}`,
      tone: user.novelAiSubscriptionActive ? ('success' as const) : ('warning' as const),
      icon: 'check_circle',
    };
  });

  protected toggleSidenav(): void {
    this.collapsed.update((value) => !value);
  }

  protected async signOut(): Promise<void> {
    this.auth.logout();
    await this.router.navigate(['/login']);
  }
}
