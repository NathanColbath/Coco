import { DatePipe } from '@angular/common';
import { Component, inject, resource, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { api, unwrap, type ComicSummary } from '../../core/api/api';
import {
  ConfirmDialogService,
  EmptyState,
  PageHeader,
  SectionCard,
  StatusChip,
} from '../../shared/ui';

/** Shape of `GET /api/comics/{projectId}/deletion-preview` until the schema is regenerated. */
interface ComicDeletionPreview {
  pages: number;
  images: number;
  bytes: number;
  hasCover: boolean;
  curationRuns: number;
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
  selector: 'cs-comics',
  imports: [
    DatePipe,
    RouterLink,
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatMenuModule,
    MatProgressBarModule,
    MatSelectModule,
    PageHeader,
    SectionCard,
    StatusChip,
    EmptyState,
  ],
  templateUrl: './comics.html',
  styleUrl: './comics.scss',
})
export class Comics {
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);
  private readonly confirmDialog = inject(ConfirmDialogService);

  protected readonly comics = resource({
    loader: () => unwrap(api.GET('/api/comics')),
  });

  protected readonly stories = resource({
    loader: () => unwrap(api.GET('/api/stories')),
  });

  protected readonly creating = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  /** Id of the comic whose delete flow is in flight, so the menu cannot fire twice. */
  protected readonly removing = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    storyId: ['', Validators.required],
    title: [''],
    premise: [''],
  });

  protected async create(): Promise<void> {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }

    const { storyId, title, premise } = this.form.getRawValue();
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);

    try {
      const comic = await unwrap(
        api.POST('/api/comics', {
          body: {
            storyId,
            title: title || null,
            premise: premise || null,
          },
        }),
      );
      await this.router.navigate(['/comics', comic.id]);
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Could not create the comic.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(comic: ComicSummary): Promise<void> {
    if (this.removing()) {
      return;
    }

    this.removing.set(comic.id);
    this.error.set(null);
    this.notice.set(null);

    try {
      const preview = await this.deletionPreview(comic.id);
      const confirmed = await this.confirmDialog.confirm({
        title: `Delete "${comic.title}"?`,
        message:
          'The comic, its pages, generated art and curation history are permanently deleted from disk.',
        danger: true,
        confirmLabel: 'Delete',
        impacts: this.deletionImpacts(preview),
      });

      if (!confirmed) {
        return;
      }

      await unwrap(
        api.DELETE('/api/comics/{projectId}', { params: { path: { projectId: comic.id } } }),
      );
      this.notice.set(`Deleted “${comic.title}”.`);
      this.comics.reload();
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : `Could not delete “${comic.title}”.`);
    } finally {
      this.removing.set(null);
    }
  }

  private async deletionPreview(projectId: string): Promise<ComicDeletionPreview> {
    // The endpoint post-dates schema.d.ts; the cast goes away with the next `npm run api:types`.
    return unwrap<ComicDeletionPreview>(
      api.GET('/api/comics/{projectId}/deletion-preview' as any, {
        params: { path: { projectId } },
      }) as any,
    );
  }

  private deletionImpacts(preview: ComicDeletionPreview): DeletionImpact[] {
    const impacts: DeletionImpact[] = [];

    if (preview.pages > 0) {
      impacts.push({ label: plural(preview.pages, 'page') });
    }
    if (preview.images > 0) {
      impacts.push({ label: plural(preview.images, 'image'), detail: formatBytes(preview.bytes) });
    }
    if (preview.hasCover) {
      impacts.push({ label: 'Generated cover' });
    }
    if (preview.curationRuns > 0) {
      impacts.push({ label: plural(preview.curationRuns, 'curation run') });
    }
    for (const cascade of preview.cascades ?? []) {
      impacts.push({
        label: cascade.title,
        detail: `${cascade.kind} · ${cascade.pages} pages · ${cascade.images} images`,
      });
    }

    return impacts;
  }
}
