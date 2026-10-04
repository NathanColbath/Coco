import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, inject, resource, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { api, unwrap, type StorySummary } from '../../core/api/api';
import {
  ConfirmDialogService,
  EmptyState,
  PageHeader,
  SectionCard,
  StatusChip,
} from '../../shared/ui';

/** Shape of `GET /api/stories/{storyId}/deletion-preview` until the schema is regenerated. */
interface StoryDeletionPreview {
  characters: number;
  blocks: number;
  lorebookEntries: number;
  comics: number;
  pages: number;
  images: number;
  bytes: number;
  cascades: DeletionCascade[];
}

interface DeletionCascade {
  kind: string;
  title: string;
  pages: number;
  images: number;
  bytes: number;
}

interface DeletionImpact {
  label: string;
  detail?: string;
}

function plural(count: number, singular: string, pluralForm = `${singular}s`): string {
  return `${count} ${count === 1 ? singular : pluralForm}`;
}

function formatBytes(bytes: number): string {
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let value = Math.max(bytes, 0);
  let unit = 0;

  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }

  const rounded = unit === 0 || value >= 100 ? Math.round(value) : Math.round(value * 10) / 10;
  return `${rounded} ${units[unit]}`;
}

@Component({
  selector: 'cs-stories',
  imports: [
    DatePipe,
    DecimalPipe,
    RouterLink,
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatMenuModule,
    MatProgressBarModule,
    PageHeader,
    SectionCard,
    StatusChip,
    EmptyState,
  ],
  templateUrl: './stories.html',
  styleUrl: './stories.scss',
})
export class Stories {
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);
  private readonly confirmDialog = inject(ConfirmDialogService);

  protected readonly stories = resource({
    loader: () => unwrap(api.GET('/api/stories')),
  });

  protected readonly creating = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  /** Id of the story whose delete flow is in flight, so the menu cannot fire twice. */
  protected readonly removing = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    title: ['', Validators.required],
    synopsis: [''],
    genre: [''],
    tags: [''],
  });

  protected async create(): Promise<void> {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }

    const { title, synopsis, genre, tags } = this.form.getRawValue();
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);

    try {
      const story = await unwrap(
        api.POST('/api/stories', {
          body: {
            title,
            synopsis: synopsis || null,
            genre: genre || null,
            tags: tags || null,
            textModelId: null,
          },
        }),
      );
      await this.router.navigate(['/stories', story.id]);
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Could not create the story.');
    } finally {
      this.busy.set(false);
    }
  }

  protected toggleCreate(): void {
    this.creating.update((value) => !value);
    this.error.set(null);
    this.notice.set(null);
  }

  protected async remove(story: StorySummary): Promise<void> {
    if (this.removing()) {
      return;
    }

    this.removing.set(story.id);
    this.error.set(null);
    this.notice.set(null);

    try {
      const preview = await this.deletionPreview(story.id);
      const confirmed = await this.confirmDialog.confirm({
        title: `Delete "${story.title}"?`,
        message:
          'The story, its characters, lorebook entries and the comics adapted from it are permanently deleted from disk.',
        danger: true,
        confirmLabel: 'Delete',
        impacts: this.deletionImpacts(preview),
        requireAcknowledgement: preview.comics > 0,
      });

      if (!confirmed) {
        return;
      }

      await unwrap(
        api.DELETE('/api/stories/{storyId}', { params: { path: { storyId: story.id } } }),
      );
      this.notice.set(`Deleted “${story.title}”.`);
      this.stories.reload();
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : `Could not delete “${story.title}”.`);
    } finally {
      this.removing.set(null);
    }
  }

  private async deletionPreview(storyId: string): Promise<StoryDeletionPreview> {
    // The endpoint post-dates schema.d.ts; the cast goes away with the next `npm run api:types`.
    return unwrap<StoryDeletionPreview>(
      api.GET('/api/stories/{storyId}/deletion-preview' as any, {
        params: { path: { storyId } },
      }) as any,
    );
  }

  private deletionImpacts(preview: StoryDeletionPreview): DeletionImpact[] {
    const impacts: DeletionImpact[] = [];

    for (const cascade of preview.cascades ?? []) {
      impacts.push({
        label: cascade.title,
        detail: `${cascade.kind} · ${cascade.pages} pages · ${cascade.images} images`,
      });
    }
    if (preview.characters > 0) {
      impacts.push({ label: plural(preview.characters, 'character') });
    }
    if (preview.blocks > 0) {
      impacts.push({ label: plural(preview.blocks, 'block') });
    }
    if (preview.lorebookEntries > 0) {
      impacts.push({
        label: plural(preview.lorebookEntries, 'lorebook entry', 'lorebook entries'),
      });
    }
    if (preview.comics > 0) {
      impacts.push({ label: plural(preview.comics, 'comic') });
    }
    if (preview.pages > 0) {
      impacts.push({ label: plural(preview.pages, 'page') });
    }
    if (preview.images > 0) {
      impacts.push({ label: plural(preview.images, 'image'), detail: formatBytes(preview.bytes) });
    }

    return impacts;
  }
}
