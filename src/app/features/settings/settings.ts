import { Component, computed, effect, inject, resource, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { DatePipe, DecimalPipe } from '@angular/common';
import { api, unwrap, type NovelAiConnection } from '../../core/api/api';
import { AuthService } from '../../core/auth/auth.service';
import {
  contextTokensFor,
  eligibleModels,
  tierLabel,
} from '../../core/models/model-registry';
import { NovelAiNotice, PageHeader, SectionCard, StatusChip } from '../../shared/ui';

@Component({
  selector: 'cs-settings',
  imports: [
    DatePipe,
    DecimalPipe,
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatSelectModule,
    PageHeader,
    SectionCard,
    StatusChip,
    NovelAiNotice,
  ],
  templateUrl: './settings.html',
  styleUrl: './settings.scss',
})
export class Settings {
  private readonly auth = inject(AuthService);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly user = this.auth.user;
  protected readonly tierLabel = tierLabel;

  protected readonly connection = signal<NovelAiConnection | null>(null);
  protected readonly loadingConnection = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly tokenInput = signal('');
  protected readonly showToken = signal(false);

  protected readonly models = resource({ loader: () => unwrap(api.GET('/api/models')) });

  protected readonly eligibleTextModels = computed(() =>
    eligibleModels(this.models.value() ?? [], 'Text', this.auth.tier()),
  );
  protected readonly eligibleImageModels = computed(() =>
    eligibleModels(this.models.value() ?? [], 'Image', this.auth.tier()),
  );

  protected readonly preferenceForm = this.formBuilder.group({
    preferredTextModelId: this.formBuilder.control<string | null>(null),
    preferredImageModelId: this.formBuilder.control<string | null>(null),
  });

  constructor() {
    void this.loadConnection();

    effect(() => {
      const user = this.auth.user();
      this.preferenceForm.patchValue(
        {
          preferredTextModelId: user?.preferredTextModelId ?? null,
          preferredImageModelId: user?.preferredImageModelId ?? null,
        },
        { emitEvent: false },
      );
    });
  }

  protected contextHint(modelId: string | null): string {
    const model = (this.models.value() ?? []).find((item) => item.id === modelId);
    if (!model) {
      return '';
    }
    const tokens = contextTokensFor(model, this.auth.tier());
    return tokens ? `${tokens.toLocaleString()} token context` : '';
  }

  protected async connect(): Promise<void> {
    const token = this.tokenInput().trim();
    if (!token) {
      this.error.set('Paste your NovelAI persistent API token first.');
      return;
    }

    await this.run(async () => {
      const connection = await unwrap(
        api.PUT('/api/settings/novelai', { body: { token } }),
      );
      this.connection.set(connection);
      this.tokenInput.set('');
      this.notice.set(`Connected to NovelAI as ${tierLabel(connection.tier)}.`);
      await this.auth.refreshUser();
      this.models.reload();
    });
  }

  protected async refresh(): Promise<void> {
    await this.run(async () => {
      const connection = await unwrap(api.POST('/api/settings/novelai/refresh'));
      this.connection.set(connection);
      this.notice.set(
        connection.subscriptionActive
          ? 'Subscription refreshed.'
          : (connection.message ?? 'NovelAI did not report an active subscription.'),
      );
      await this.auth.refreshUser();
    });
  }

  protected async disconnect(): Promise<void> {
    await this.run(async () => {
      const connection = await unwrap(api.DELETE('/api/settings/novelai'));
      this.connection.set(connection);
      this.notice.set('NovelAI account disconnected.');
      await this.auth.refreshUser();
    });
  }

  protected async savePreferences(): Promise<void> {
    const { preferredTextModelId, preferredImageModelId } = this.preferenceForm.getRawValue();
    await this.run(async () => {
      await unwrap(
        api.PUT('/api/settings/preferences', {
          body: { preferredTextModelId, preferredImageModelId },
        }),
      );
      await this.auth.refreshUser();
      this.notice.set('Defaults saved.');
    });
  }

  private async loadConnection(): Promise<void> {
    this.loadingConnection.set(true);
    try {
      this.connection.set(await unwrap(api.GET('/api/settings/novelai')));
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Could not load settings.');
    } finally {
      this.loadingConnection.set(false);
    }
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    try {
      await action();
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Something went wrong.');
    } finally {
      this.busy.set(false);
    }
  }
}
