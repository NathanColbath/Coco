import { Component, computed, inject, input, resource, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { api, unwrap, type LorebookEntry } from '../../../core/api/api';
import { EmptyState, PageHeader, SectionCard, StatusChip } from '../../../shared/ui';

@Component({
  selector: 'cs-lorebook',
  imports: [
    RouterLink,
    ReactiveFormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatSelectModule,
    MatTooltipModule,
    PageHeader,
    SectionCard,
    StatusChip,
    EmptyState,
  ],
  templateUrl: './lorebook.html',
  styleUrl: './lorebook.scss',
})
export class Lorebook {
  private readonly formBuilder = inject(FormBuilder);

  protected readonly storyId = input.required<string>();

  protected readonly entries = resource({
    loader: () =>
      unwrap(
        api.GET('/api/stories/{storyId}/lorebook', {
          params: { path: { storyId: this.storyId() } },
        }),
      ),
  });

  protected readonly selectedId = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  protected readonly selected = computed(
    () => (this.entries.value() ?? []).find((entry) => entry.id === this.selectedId()) ?? null,
  );

  protected readonly form = this.formBuilder.nonNullable.group({
    displayName: [''],
    keys: [''],
    content: [''],
    category: [''],
    enabled: [true],
    alwaysOn: [false],
    insertionOrder: [100],
    insertionPosition: [0],
    prefix: [''],
    suffix: [''],
    reservedTokens: [0],
    trimDirection: ['DoNotTrim' as string],
    keyRelative: [false],
  });

  protected startNew(): void {
    this.selectedId.set(null);
    this.form.reset({
      displayName: '',
      keys: '',
      content: '',
      category: '',
      enabled: true,
      alwaysOn: false,
      insertionOrder: 100,
      insertionPosition: 0,
      prefix: '',
      suffix: '',
      reservedTokens: 0,
      trimDirection: 'DoNotTrim',
      keyRelative: false,
    });
    this.notice.set(null);
    this.error.set(null);
  }

  protected select(entry: LorebookEntry): void {
    this.selectedId.set(entry.id);
    this.form.reset({
      displayName: entry.displayName,
      keys: entry.keys.join('\n'),
      content: entry.content,
      category: entry.category ?? '',
      enabled: entry.enabled,
      alwaysOn: entry.alwaysOn,
      insertionOrder: Number(entry.insertionOrder),
      insertionPosition: Number(entry.insertionPosition),
      prefix: entry.prefix,
      suffix: entry.suffix,
      reservedTokens: Number(entry.reservedTokens),
      trimDirection: entry.trimDirection as string,
      keyRelative: entry.keyRelative,
    });
    this.notice.set(null);
    this.error.set(null);
  }

  protected async save(): Promise<void> {
    const values = this.form.getRawValue();
    if (!values.displayName.trim()) {
      this.error.set('Give the entry a name.');
      return;
    }

    const body = {
      displayName: values.displayName,
      keys: values.keys,
      content: values.content,
      category: values.category || null,
      alwaysOn: values.alwaysOn,
      insertionOrder: Number(values.insertionOrder) || 0,
      insertionPosition: Number(values.insertionPosition) || 0,
      prefix: values.prefix,
      suffix: values.suffix,
      reservedTokens: Number(values.reservedTokens) || 0,
      trimDirection: values.trimDirection as LorebookEntry['trimDirection'],
      keyRelative: values.keyRelative,
      enabled: values.enabled,
    };

    await this.run(async () => {
      const id = this.selectedId();
      if (id) {
        const updated = await unwrap(
          api.PUT('/api/stories/{storyId}/lorebook/{entryId}', {
            params: { path: { storyId: this.storyId(), entryId: id } },
            body,
          }),
        );
        this.selectedId.set(updated.id);
        this.notice.set('Entry saved.');
      } else {
        const created = await unwrap(
          api.POST('/api/stories/{storyId}/lorebook', {
            params: { path: { storyId: this.storyId() } },
            body,
          }),
        );
        this.selectedId.set(created.id);
        this.notice.set('Entry created.');
      }

      this.entries.reload();
    });
  }

  protected async remove(entry: LorebookEntry): Promise<void> {
    await this.run(async () => {
      await unwrap(
        api.DELETE('/api/stories/{storyId}/lorebook/{entryId}', {
          params: { path: { storyId: this.storyId(), entryId: entry.id } },
        }),
      );
      if (this.selectedId() === entry.id) {
        this.startNew();
      }
      this.notice.set('Entry deleted.');
      this.entries.reload();
    });
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Something went wrong.');
    } finally {
      this.busy.set(false);
    }
  }
}
