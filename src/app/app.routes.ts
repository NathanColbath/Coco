import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login/login').then((m) => m.Login),
    title: 'Sign in · Comic Studio',
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./core/layout/app-shell').then((m) => m.AppShell),
    children: [
      {
        path: 'dashboard',
        loadComponent: () =>
          import('./features/dashboard/dashboard').then((m) => m.Dashboard),
        title: 'Dashboard · Comic Studio',
      },
      {
        path: 'stories',
        loadComponent: () =>
          import('./features/stories/stories').then((m) => m.Stories),
        title: 'Stories · Comic Studio',
      },
      {
        path: 'stories/:storyId',
        loadComponent: () =>
          import('./features/stories/editor/story-editor').then((m) => m.StoryEditor),
        title: 'Story · Comic Studio',
      },
      {
        path: 'stories/:storyId/lorebook',
        loadComponent: () =>
          import('./features/stories/lorebook/lorebook').then((m) => m.Lorebook),
        title: 'Lorebook · Comic Studio',
      },
      {
        path: 'projects',
        loadComponent: () =>
          import('./features/comics/comics').then((m) => m.Comics),
        title: 'Comics · Comic Studio',
      },
      {
        path: 'comics',
        loadComponent: () =>
          import('./features/comics/comics').then((m) => m.Comics),
        title: 'Comics · Comic Studio',
      },
      {
        path: 'comics/:projectId',
        loadComponent: () =>
          import('./features/comics/workspace/workspace').then((m) => m.ComicWorkspace),
        title: 'Comic workspace · Comic Studio',
      },
      {
        path: 'comics/:projectId/read',
        loadComponent: () =>
          import('./features/comics/reader/reader').then((m) => m.ComicReader),
        title: 'Read · Comic Studio',
      },
      {
        path: 'settings',
        loadComponent: () =>
          import('./features/settings/settings').then((m) => m.Settings),
        title: 'Settings · Comic Studio',
      },
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: '**',
        loadComponent: () =>
          import('./features/placeholder/placeholder').then((m) => m.Placeholder),
        data: {
          headline: 'Not found',
          description: 'That page does not exist.',
          icon: 'help',
        },
        title: 'Not found · Comic Studio',
      },
    ],
  },
];
