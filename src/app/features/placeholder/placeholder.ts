import { Component, input } from '@angular/core';
import { EmptyState, PageHeader } from '../../shared/ui';

@Component({
  selector: 'cs-placeholder',
  imports: [PageHeader, EmptyState],
  template: `
    <div class="cs-page">
      <cs-page-header [heading]="headline()" [subtitle]="description()" />
      <cs-empty-state
        [icon]="icon()"
        heading="Coming in a later milestone"
        message="This area is scaffolded and wired into routing."
      />
    </div>
  `,
})
export class Placeholder {
  readonly headline = input.required<string>();
  readonly description = input<string>();
  readonly icon = input('construction');
}
