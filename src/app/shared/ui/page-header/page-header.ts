import { Component, input } from '@angular/core';

@Component({
  selector: 'cs-page-header',
  templateUrl: './page-header.html',
  styleUrl: './page-header.scss',
})
export class PageHeader {
  readonly heading = input.required<string>();
  readonly subtitle = input<string>();
}
