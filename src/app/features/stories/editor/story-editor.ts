import { DatePipe } from '@angular/common';
import {
  Component,
  OnDestroy,
  OnInit,
  computed,
  inject,
  input,
  resource,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Subscription } from 'rxjs';
import {
  api,
  unwrap,
  type ContextTrace,
  type StoryDetail,
  type StoryGeneration,
} from '../../../core/api/api';
import { GenerationHubService } from '../../../core/realtime/generation-hub.service';
import { eligibleModels } from '../../../core/models/model-registry';
import { AuthService } from '../../../core/auth/auth.service';
import { EmptyState, NovelAiNotice, PageHeader, SectionCard, StatusChip } from '../../../shared/ui';
import { ContextTraceView } from '../context-trace/context-trace';
import { activePath, alternativeCount, wordCount } from '../story-path';

@Component({
  selector: 'cs-story-editor',
  imports: [
    DatePipe,

    RouterLink,
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatSelectModule,
    MatTabsModule,
    MatTooltipModule,
    PageHeader,
    SectionCard,
    StatusChip,
    EmptyState,
    NovelAiNotice,
    ContextTraceView,
  ],
  templateUrl: './story-editor.html',
  styleUrl: './story-editor.scss',
})
export class StoryEditor implements OnInit, OnDestroy {
  private readonly hub = inject(GenerationHubService);
  private readonly auth = inject(AuthService);
  private readonly formBuilder = inject(FormBuilder);
  private readonly subscriptions = new Subscription();

  protected readonly storyId = input.required<string>();

  protected readonly story = signal<StoryDetail | null>(null);
  protected readonly generations = signal<StoryGeneration[]>([]);
  protected readonly trace = signal<ContextTrace | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  protected readonly streaming = signal(false);
  protected readonly streamText = signal('');
  protected readonly activeGenerationId = signal<string | null>(null);
  protected readonly instruction = signal('');
  protected readonly draft = signal('');
  protected readonly editingBlockId = signal<string | null>(null);
  protected readonly editText = signal('');

  protected readonly models = resource({
    loader: () => unwrap(api.GET('/api/models', { params: { query: { kind: 'Text' } } })),
  });

  protected readonly textModels = computed(() =>
    eligibleModels(this.models.value() ?? [], 'Text', this.auth.tier()),
  );

  protected readonly path = computed(() =>
    activePath(this.story()?.blocks ?? [], this.story()?.cursorBlockId ?? null),
  );

  protected readonly words = computed(() => wordCount(this.story()?.activeText ?? ''));

  protected readonly variants = computed(() => {
    const story = this.story();
    if (!story) {
      return [];
    }
    const activeIds = new Set(this.path().map((block) => block.id));
    return story.blocks
      .filter((block) => !activeIds.has(block.id))
      .sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  });

  protected readonly abandonedCount = computed(() =>
    alternativeCount(this.story()?.blocks ?? [], this.path()),
  );

  protected readonly settingsForm = this.formBuilder.nonNullable.group({
    title: [''],
    synopsis: [''],
    genre: [''],
    tags: [''],
    textModelId: [''],
    systemPrompt: [''],
    memory: [''],
    authorNote: [''],
    authorNoteDepth: [3],
    temperature: [0.85],
    maxTokens: [160],
    topP: [0.95],
  });

  constructor() {
    this.subscriptions.add(
      this.hub.started$.subscribe((event) => {
        if (event.generationId === this.activeGenerationId()) {
          this.notice.set(`Generating with ${event.model}…`);
        }
      }),
    );

    this.subscriptions.add(
      this.hub.token$.subscribe((event) => {
        if (event.generationId === this.activeGenerationId()) {
          this.streamText.update((text) => text + event.text);
        }
      }),
    );

    this.subscriptions.add(
      this.hub.completed$.subscribe(async (event) => {
        if (event.generationId !== this.activeGenerationId()) {
          return;
        }
        this.streaming.set(false);
        this.streamText.set('');
        this.activeGenerationId.set(null);
        this.notice.set(`Finished in ${(event.durationMs / 1000).toFixed(1)}s.`);
        await this.load();
      }),
    );

    this.subscriptions.add(
      this.hub.failed$.subscribe((event) => {
        if (event.generationId !== this.activeGenerationId()) {
          return;
        }
        this.streaming.set(false);
        this.streamText.set('');
        this.activeGenerationId.set(null);
        this.error.set(event.message);
      }),
    );
  }

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
  }

  // --- Actions ------------------------------------------------------------

  protected async appendDraft(): Promise<void> {
    const story = this.story();
    const text = this.draft().trim();
    if (!story || text.length === 0) {
      return;
    }

    await this.run(async () => {
      await unwrap(
        api.POST('/api/stories/{storyId}/blocks', {
          params: { path: { storyId: story.id } },
          body: { text, kind: 'Prose' },
        }),
      );
      this.draft.set('');
      await this.load();
    });
  }

  protected async addSceneBreak(): Promise<void> {
    const story = this.story();
    if (!story) {
      return;
    }

    await this.run(async () => {
      await unwrap(
        api.POST('/api/stories/{storyId}/blocks', {
          params: { path: { storyId: story.id } },
          body: { text: '***', kind: 'SceneBreak' },
        }),
      );
      await this.load();
    });
  }

  protected async generate(): Promise<void> {
    const story = this.story();
    if (!story || this.streaming()) {
      return;
    }

    this.error.set(null);
    this.notice.set(null);
    this.busy.set(true);
    this.streamText.set('');

    try {
      await this.hub.ensureConnected();
      const accepted = await unwrap(
        api.POST('/api/stories/{storyId}/generate', {
          params: { path: { storyId: story.id } },
          body: {
            instruction: this.instruction().trim() || null,
            maxTokens: null,
            modelId: null,
          },
        }),
      );
      this.activeGenerationId.set(accepted.generationId);
      this.streaming.set(true);
      this.instruction.set('');
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Could not start generation.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async retry(): Promise<void> {
    const lastAi = [...this.path()].reverse().find((block) => block.origin === 'Ai');
    if (!lastAi) {
      this.error.set('There is no AI passage to retry yet.');
      return;
    }

    await this.setCursor(lastAi.parentId ?? null);
    await this.generate();
  }

  protected async undo(): Promise<void> {
    const last = this.path().at(-1);
    if (!last) {
      return;
    }
    await this.setCursor(last.parentId ?? null);
  }

  protected async switchTo(blockId: string): Promise<void> {
    await this.setCursor(blockId);
  }

  protected startEdit(blockId: string, text: string): void {
    this.editingBlockId.set(blockId);
    this.editText.set(text);
  }

  protected cancelEdit(): void {
    this.editingBlockId.set(null);
    this.editText.set('');
  }

  /**
   * Saving an edited block moves the cursor to it, so the passages that were
   * generated from the old text become an alternative branch rather than
   * silently contradicting the edit.
   */
  protected async saveEdit(): Promise<void> {
    const story = this.story();
    const blockId = this.editingBlockId();
    if (!story || !blockId) {
      return;
    }

    await this.run(async () => {
      await unwrap(
        api.PUT('/api/stories/{storyId}/blocks/{blockId}', {
          params: { path: { storyId: story.id, blockId } },
          body: { text: this.editText() },
        }),
      );
      await unwrap(
        api.POST('/api/stories/{storyId}/cursor', {
          params: { path: { storyId: story.id } },
          body: { blockId },
        }),
      );
      this.cancelEdit();
      await this.load();
    });
  }

  protected async previewContext(): Promise<void> {
    const story = this.story();
    if (!story) {
      return;
    }

    await this.run(async () => {
      const preview = await unwrap(
        api.POST('/api/stories/{storyId}/context-preview', {
          params: { path: { storyId: story.id } },
          body: { instruction: this.instruction().trim() || null },
        }),
      );
      this.trace.set(preview.trace);
      this.notice.set('Context assembled — nothing was sent to NovelAI.');
    });
  }

  protected async saveSettings(): Promise<void> {
    const story = this.story();
    if (!story) {
      return;
    }

    const values = this.settingsForm.getRawValue();

    await this.run(async () => {
      const updated = await unwrap(
        api.PUT('/api/stories/{storyId}', {
          params: { path: { storyId: story.id } },
          body: {
            title: values.title,
            synopsis: values.synopsis || null,
            genre: values.genre || null,
            tags: values.tags || null,
            textModelId: values.textModelId || null,
            context: {
              ...story.context,
              systemPrompt: values.systemPrompt || null,
              memory: values.memory || null,
              authorNote: values.authorNote || null,
              authorNoteDepth: Number(values.authorNoteDepth) || 3,
            },
            sampler: {
              ...story.sampler,
              temperature: Number(values.temperature),
              maxTokens: Number(values.maxTokens),
              topP: Number(values.topP),
            },
          },
        }),
      );
      this.story.set(updated);
      this.applyForm(updated);
      this.notice.set('Story settings saved.');
    });
  }

  // --- Loading ------------------------------------------------------------

  private async setCursor(blockId: string | null): Promise<void> {
    const story = this.story();
    if (!story) {
      return;
    }

    await this.run(async () => {
      const updated = await unwrap(
        api.POST('/api/stories/{storyId}/cursor', {
          params: { path: { storyId: story.id } },
          body: { blockId },
        }),
      );
      this.story.set(updated);
    });
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const story = await unwrap(
        api.GET('/api/stories/{storyId}', {
          params: { path: { storyId: this.storyId() } },
        }),
      );
      this.story.set(story);
      this.applyForm(story);

      const generations = await unwrap(
        api.GET('/api/stories/{storyId}/generations', {
          params: { path: { storyId: this.storyId() }, query: { take: 10 } },
        }),
      );
      this.generations.set(generations);
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Could not load the story.');
    } finally {
      this.loading.set(false);
    }
  }

  private applyForm(story: StoryDetail): void {
    this.settingsForm.patchValue({
      title: story.title,
      synopsis: story.synopsis ?? '',
      genre: story.genre ?? '',
      tags: story.tags ?? '',
      textModelId: story.textModelId ?? '',
      systemPrompt: story.context.systemPrompt ?? '',
      memory: story.context.memory ?? '',
      authorNote: story.context.authorNote ?? '',
      authorNoteDepth: Number(story.context.authorNoteDepth ?? 3),
      temperature: Number(story.sampler.temperature ?? 0.85),
      maxTokens: Number(story.sampler.maxTokens ?? 160),
      topP: Number(story.sampler.topP ?? 0.95),
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
