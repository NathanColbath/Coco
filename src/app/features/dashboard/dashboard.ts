import { DatePipe } from '@angular/common';
import { Component, computed, resource } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { api, type AiModel } from '../../core/api/api';
import { EmptyState, PageHeader, SectionCard, StatusChip } from '../../shared/ui';

@Component({
  selector: 'cs-dashboard',
  imports: [
    DatePipe,
    PageHeader,
    SectionCard,
    StatusChip,
    EmptyState,
    MatIconModule,
    MatButtonModule,
  ],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard {
  protected readonly health = resource({
    loader: async () => {
      const { data, error } = await api.GET('/api/health');
      if (error) throw new Error('The API did not respond.');
      return data;
    },
  });

  protected readonly models = resource({
    loader: async () => {
      const { data, error } = await api.GET('/api/models');
      if (error) throw new Error('Could not load the model registry.');
      return data;
    },
  });

  protected readonly textModels = computed(() => this.byKind('Text'));
  protected readonly imageModels = computed(() => this.byKind('Image'));

  protected reload(): void {
    this.health.reload();
    this.models.reload();
  }

  protected tokenLabel(tokens: number | string | null | undefined): string {
    const value = typeof tokens === 'string' ? Number(tokens) : tokens;
    if (!value || Number.isNaN(value)) {
      return 'unknown';
    }
    return value >= 1000 ? `${Math.round(value / 1000)}k` : String(value);
  }

  private byKind(kind: AiModel['kind']): AiModel[] {
    return (this.models.value() ?? []).filter((model) => model.kind === kind);
  }
}
