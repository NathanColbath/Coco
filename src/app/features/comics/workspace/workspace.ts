import { Component, OnDestroy, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Subscription } from 'rxjs';
import {
  api,
  unwrap,
  type ComicDetail,
  type ContentRating,
  type DialogueKind,
  type DialogueLine,
  type Page,
} from '../../../core/api/api';
import { GenerationHubService } from '../../../core/realtime/generation-hub.service';
import {
  AuthedImage,
  NovelAiNotice,
  PageHeader,
  SectionCard,
  StatusChip,
  type ChipTone,
} from '../../../shared/ui';

interface PageDraft {
  script: string;
  continuity: string;
  sceneTitle: string;
  modelOverride: string | null;
  dialogue: Array<{ kind: DialogueKind; speakerCharacterId: string | null; text: string; position: string }>;
}

/**
 * Page fields the API is gaining for the single-image pipeline. They are read
 * defensively because the generated schema may not carry them yet.
 */
interface PageExtras {
  environmentName?: string | null;
  panelPlanJson?: string | null;
  modelUsed?: string | null;
  modelOverride?: string | null;
}

interface PanelPlanCast {
  name?: string | null;
  pose?: string | null;
  expression?: string | null;
  facing?: string | null;
  prominence?: string | null;
}

interface PanelPlanPanel {
  order?: number | string | null;
  framing?: string | null;
  action?: string | null;
  background?: string | null;
  cast?: PanelPlanCast[];
  props?: string[];
}

interface LedgerSection {
  heading: string | null;
  text: string;
}

interface ModelBadge {
  label: string;
  tone: ChipTone;
  reason: string;
}

const V4_MODEL = 'nai-diffusion-4-5-full';
const V5_MODEL = 'nai-diffusion-5-full';
const LEDGER_HEADING = /^(LOCATION|CAST|PROPS|THREADS|PALETTE|LAST BEAT)\s*:?\s*(.*)$/i;

@Component({
  selector: 'cs-comic-workspace',
  imports: [
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatSelectModule,
    MatTooltipModule,
    PageHeader,
    SectionCard,
    StatusChip,
    NovelAiNotice,
    AuthedImage,
  ],
  templateUrl: './workspace.html',
  styleUrl: './workspace.scss',
})
export class ComicWorkspace implements OnInit, OnDestroy {
  private readonly hub = inject(GenerationHubService);
  private readonly subscriptions = new Subscription();

  readonly projectId = input.required<string>();

  protected readonly ratings = [
    { value: 'General', label: 'General — nothing adult' },
    { value: 'Mature', label: 'Mature — suggestive, adult themes' },
    { value: 'Explicit', label: 'Explicit — no filtering' },
  ];

  protected readonly kinds: DialogueKind[] = ['Speech', 'Thought', 'Narration', 'Sfx'];

  protected readonly modelOverrides: Array<{ value: string | null; label: string }> = [
    { value: null, label: 'Automatic — V5 when the page has dialogue' },
    { value: V4_MODEL, label: 'V4.5 — nai-diffusion-4-5-full' },
    { value: V5_MODEL, label: 'V5 — nai-diffusion-5-full' },
  ];

  protected readonly comic = signal<ComicDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly curation = signal<{ percent: number; message: string } | null>(null);
  protected readonly inFlight = signal<string[]>([]);
  protected readonly openPageId = signal<string | null>(null);

  protected readonly pages = computed<Array<Page & PageExtras>>(
    () => (this.comic()?.pages ?? []) as Array<Page & PageExtras>,
  );

  protected draft: PageDraft | null = null;

  protected readonly progress = computed(() => {
    const pages = this.pages();
    return {
      drawn: pages.filter((page) => page.status === 'Generated').length,
      total: pages.length,
    };
  });

  constructor() {
    this.subscriptions.add(
      this.hub.imageStarted$.subscribe((event) => {
        const id = event.targetId;
        if (id) {
          this.inFlight.update((ids) => [...new Set([...ids, id])]);
        }
      }),
    );

    this.subscriptions.add(
      this.hub.imageCompleted$.subscribe(async () => {
        this.inFlight.set([]);
        await this.load();
      }),
    );

    this.subscriptions.add(
      this.hub.imageFailed$.subscribe((event) => {
        this.inFlight.set([]);
        this.error.set(event.message ?? 'Image generation failed.');
      }),
    );

    this.subscriptions.add(
      this.hub.curationProgress$.subscribe(async (event) => {
        if (event.projectId !== this.projectId()) {
          return;
        }

        this.curation.set({ percent: event.percent, message: event.message });

        if (event.failed) {
          this.error.set(event.message);
          this.curation.set(null);
          await this.load();
          return;
        }

        if (event.percent >= 100) {
          this.curation.set(null);
          this.notice.set(event.message);
          await this.load();
        }
      }),
    );
  }

  async ngOnInit(): Promise<void> {
    await this.hub.ensureConnected();
    await this.load();
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
  }

  // --- Page editing -------------------------------------------------------

  protected open(page: Page): void {
    this.openPageId.set(page.id);
    this.draft = {
      script: page.script ?? '',
      continuity: page.continuity ?? '',
      sceneTitle: page.sceneTitle ?? '',
      modelOverride: this.modelOverrideOf(page),
      dialogue: (page.dialogue ?? []).map((line) => ({
        kind: line.kind,
        speakerCharacterId: line.speakerCharacterId ?? null,
        text: line.text,
        position: line.position ?? 'upper right',
      })),
    };
  }

  protected close(): void {
    this.openPageId.set(null);
    this.draft = null;
  }

  protected addLine(): void {
    this.draft?.dialogue.push({
      kind: 'Speech',
      speakerCharacterId: null,
      text: '',
      position: 'upper right',
    });
  }

  protected removeLine(index: number): void {
    this.draft?.dialogue.splice(index, 1);
  }

  protected async savePage(pageId: string): Promise<void> {
    const draft = this.draft;
    if (!draft) {
      return;
    }

    await this.run(async () => {
      // `modelOverride` is newer than the generated request type, so the body
      // is built as a variable to stay assignable until the schema catches up.
      const body = {
        script: draft.script,
        continuity: draft.continuity,
        sceneTitle: draft.sceneTitle,
        modelOverride: draft.modelOverride,
        dialogue: draft.dialogue.map((line) => ({
          kind: line.kind,
          speakerCharacterId: line.speakerCharacterId,
          text: line.text,
          position: line.position,
        })),
      };

      await unwrap(
        api.PUT('/api/comics/{projectId}/pages/{pageId}', {
          params: { path: { projectId: this.projectId(), pageId } },
          body,
        }),
      );

      this.notice.set('Page saved and its prompt recomposed.');
      await this.load();
    });
  }

  // --- Actions ------------------------------------------------------------

  protected async curate(): Promise<void> {
    this.error.set(null);
    this.notice.set(null);
    this.curation.set({ percent: 0, message: 'Starting…' });

    await this.run(async () => {
      await unwrap(
        api.POST('/api/comics/{projectId}/curate', {
          params: { path: { projectId: this.projectId() } },
        }),
      );
    });
  }

  protected async setRating(rating: ContentRating): Promise<void> {
    const comic = this.comic();
    if (!comic || comic.rating === rating) {
      return;
    }

    await this.run(async () => {
      const updated = await unwrap(
        api.PUT('/api/comics/{projectId}', {
          params: { path: { projectId: comic.id } },
          body: {
            title: comic.title,
            premise: comic.premise,
            direction: comic.direction,
            colorMode: comic.colorMode,
            rating,
            dialogueAdaptation: comic.dialogueAdaptation,
            pageWidth: comic.pageWidth,
            pageHeight: comic.pageHeight,
            style: comic.style,
            styleAnchorImageId: comic.styleAnchorImageId ?? null,
          },
        }),
      );

      this.comic.set(updated);
      this.notice.set('Rating saved. It applies to every prompt from now on.');
    });
  }

  protected async drawPage(pageId: string): Promise<void> {
    await this.run(async () => {
      await unwrap(
        api.POST('/api/comics/{projectId}/pages/{pageId}/generate', {
          params: { path: { projectId: this.projectId(), pageId } },
          body: { highResolution: true, seed: null },
        }),
      );

      this.inFlight.update((ids) => [...new Set([...ids, pageId])]);
      this.notice.set('Drawing the page as a single image.');
    });
  }

  protected async drawAll(): Promise<void> {
    await this.run(async () => {
      const result = await unwrap(
        api.POST('/api/comics/{projectId}/generate', {
          params: { path: { projectId: this.projectId() } },
          body: { highResolution: true, includeDrawn: false },
        }),
      );

      this.notice.set(`Queued ${result.queued} page(s), drawn one at a time.`);
    });
  }

  // --- Page presentation --------------------------------------------------

  /**
   * The model this page will be drawn with, and why. A page that has already
   * been drawn reports the model it used; otherwise an explicit per-page
   * override wins; otherwise V5 is picked automatically when the page has
   * dialogue to letter, and V4.5 when it does not.
   */
  protected modelBadge(page: Page): ModelBadge {
    const used = (this.extra(page).modelUsed ?? '').trim();
    if (used) {
      return this.describeModel(used, 'drawn');
    }

    const override = this.modelOverrideOf(page);
    if (override) {
      return this.describeModel(override, 'override');
    }

    const hasDialogue = (page.dialogue ?? []).some((line) => (line.text ?? '').trim().length > 0);
    return hasDialogue
      ? {
          label: 'V5 · dialogue',
          tone: 'info',
          reason: 'V5 — automatic: this page has dialogue, so the newer model is used.',
        }
      : {
          label: 'V4.5 · no dialogue',
          tone: 'neutral',
          reason: 'V4.5 — automatic: this page has no dialogue, so the default model is used.',
        };
  }

  protected environmentName(page: Page): string | null {
    return this.extra(page).environmentName ?? null;
  }

  /** Parses the structured panel plan; malformed JSON renders nothing. */
  protected panelPlan(page: Page): PanelPlanPanel[] {
    const raw = this.extra(page).panelPlanJson;
    if (!raw) {
      return [];
    }

    try {
      const parsed = JSON.parse(raw) as { panels?: PanelPlanPanel[] } | null;
      return Array.isArray(parsed?.panels) ? parsed.panels : [];
    } catch {
      return [];
    }
  }

  protected castLine(member: PanelPlanCast): string {
    const name = (member.name ?? '').trim() || 'Unnamed';
    const details = [member.pose, member.expression, member.facing]
      .map((part) => (part ?? '').trim())
      .filter((part) => part.length > 0);

    return details.length > 0 ? `${name} — ${details.join(', ')}` : name;
  }

  /**
   * Splits the continuity ledger on its section headings. Unknown leading
   * lines are folded into an unlabelled first section so nothing is lost.
   */
  protected ledgerSections(page: Page): LedgerSection[] {
    const raw = page.continuity;
    if (!raw) {
      return [];
    }

    const sections: LedgerSection[] = [];
    let current: LedgerSection | null = null;

    for (const line of raw.split(/\r?\n/)) {
      const trimmed = line.trim();
      const match = LEDGER_HEADING.exec(trimmed);

      if (match) {
        current = { heading: match[1].toUpperCase(), text: (match[2] ?? '').trim() };
        sections.push(current);
      } else if (trimmed && current) {
        current.text = current.text ? `${current.text} ${trimmed}` : trimmed;
      } else if (trimmed) {
        current = { heading: null, text: trimmed };
        sections.push(current);
      }
    }

    return sections;
  }

  protected isDrawing(pageId: string): boolean {
    return this.inFlight().includes(pageId);
  }

  protected pageNumber(index: string | number | undefined): number {
    return Number(index ?? 0) + 1;
  }

  protected speakerName(line: DialogueLine): string {
    return line.speakerName ?? '—';
  }

  // --- Helpers ------------------------------------------------------------

  private describeModel(modelId: string, source: 'drawn' | 'override'): ModelBadge {
    const version = modelVersion(modelId);
    return {
      label: `${version} · ${source}`,
      tone: version === 'V5' ? 'info' : 'neutral',
      reason:
        source === 'drawn'
          ? `${version} — this page was drawn with ${modelId}.`
          : `${version} — manual override (${modelId}).`,
    };
  }

  private modelOverrideOf(page: Page): string | null {
    return this.extra(page).modelOverride ?? null;
  }

  private extra(page: Page): Page & PageExtras {
    return page as Page & PageExtras;
  }

  private async load(): Promise<void> {
    try {
      const comic = await unwrap(
        api.GET('/api/comics/{projectId}', {
          params: { path: { projectId: this.projectId() } },
        }),
      );
      this.comic.set(comic);
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Could not load the comic.');
    } finally {
      this.loading.set(false);
    }
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

/** V4.5's registry id contains "4-5", which must not read as a bare "5". */
function modelVersion(modelId: string): 'V5' | 'V4.5' {
  const id = modelId.toLowerCase();
  if (id.includes('4-5') || id.includes('4.5')) {
    return 'V4.5';
  }
  return id.includes('5') ? 'V5' : 'V4.5';
}
