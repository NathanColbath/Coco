import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { HostListener } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { api, unwrap, type ComicDetail } from '../../../core/api/api';
import { AuthedImage } from '../../../shared/ui';

@Component({
  selector: 'cs-comic-reader',
  imports: [
    RouterLink,
    MatButtonModule,
    MatIconModule,
    MatProgressBarModule,
    MatTooltipModule,
    AuthedImage,
  ],
  templateUrl: './reader.html',
  styleUrl: './reader.scss',
})
export class ComicReader implements OnInit {
  readonly projectId = input.required<string>();

  protected readonly comic = signal<ComicDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly index = signal(0);
  protected readonly fit = signal<'height' | 'width'>('height');
  protected readonly showCover = signal(true);

  protected readonly rightToLeft = computed(
    () => this.comic()?.direction !== 'LeftToRight',
  );

  protected readonly page = computed(() => {
    const pages = this.comic()?.pages ?? [];
    return pages[this.index()] ?? null;
  });

  protected readonly total = computed(() => this.comic()?.pages.length ?? 0);

  async ngOnInit(): Promise<void> {
    try {
      this.comic.set(
        await unwrap(
          api.GET('/api/comics/{projectId}', {
            params: { path: { projectId: this.projectId() } },
          }),
        ),
      );
    } finally {
      this.loading.set(false);
    }
  }

  @HostListener('window:keydown', ['$event'])
  protected onKey(event: KeyboardEvent): void {
    const forward = this.rightToLeft() ? 'ArrowLeft' : 'ArrowRight';
    const back = this.rightToLeft() ? 'ArrowRight' : 'ArrowLeft';

    if (event.key === forward || event.key === ' ' || event.key === 'PageDown') {
      this.next();
    } else if (event.key === back || event.key === 'PageUp') {
      this.previous();
    } else if (event.key === 'Home') {
      this.index.set(0);
    } else if (event.key === 'End') {
      this.index.set(Math.max(0, this.total() - 1));
    }
  }

  protected next(): void {
    this.index.update((value) => Math.min(this.total() - 1, value + 1));
  }

  protected previous(): void {
    this.index.update((value) => Math.max(0, value - 1));
  }

  protected toggleFit(): void {
    this.fit.update((value) => (value === 'height' ? 'width' : 'height'));
  }

  protected coverImage(): string | null | undefined {
    return this.comic()?.cover?.renderedImageId ?? this.comic()?.cover?.imageAssetId;
  }

  /** A page drawn as one image, which is the normal case. */
  protected pageImage(): string | null | undefined {
    return this.page()?.pageImageId;
  }

}
