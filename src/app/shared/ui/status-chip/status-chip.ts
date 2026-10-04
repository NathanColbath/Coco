import { Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

export type ChipTone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';

@Component({
  selector: 'cs-status-chip',
  imports: [MatIconModule],
  template: `<span class="cs-chip" [class]="'cs-chip--' + tone()">
    @if (icon()) {
      <mat-icon aria-hidden="true">{{ icon() }}</mat-icon>
    }
    {{ label() }}
  </span>`,
  styles: `
    .cs-chip {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding: 2px 10px;
      border-radius: 999px;
      font: var(--mat-sys-label-small);
      white-space: nowrap;
      background-color: var(--mat-sys-surface-container-highest);
      color: var(--mat-sys-on-surface-variant);
    }

    .cs-chip mat-icon {
      font-size: 14px;
      width: 14px;
      height: 14px;
    }

    .cs-chip--info {
      background-color: var(--mat-sys-primary-container);
      color: var(--mat-sys-on-primary-container);
    }

    .cs-chip--success {
      background-color: var(--mat-sys-tertiary-container);
      color: var(--mat-sys-on-tertiary-container);
    }

    .cs-chip--warning {
      background-color: var(--mat-sys-secondary-container);
      color: var(--mat-sys-on-secondary-container);
    }

    .cs-chip--danger {
      background-color: var(--mat-sys-error-container);
      color: var(--mat-sys-on-error-container);
    }
  `,
})
export class StatusChip {
  readonly label = input.required<string>();
  readonly tone = input<ChipTone>('neutral');
  readonly icon = input<string>();
}
