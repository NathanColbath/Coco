import { DecimalPipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { StatusChip } from '../../../shared/ui';
import type { ContextTrace } from '../../../core/api/api';

/** Renders exactly what a generation sent: budget, segments, warnings. */
@Component({
  selector: 'cs-context-trace',
  imports: [DecimalPipe, MatIconModule, StatusChip],
  templateUrl: './context-trace.html',
  styleUrl: './context-trace.scss',
})
export class ContextTraceView {
  readonly trace = input<ContextTrace | null | undefined>();

  protected readonly usedPercent = computed(() => {
    const trace = this.trace();
    if (!trace) {
      return 0;
    }
    const available = Number(trace.availableTokens ?? 0);
    const used = Number(trace.usedTokens ?? 0);
    return available === 0 ? 0 : Math.min(100, Math.round((used / available) * 100));
  });
}
