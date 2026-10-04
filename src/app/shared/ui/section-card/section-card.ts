import { Component, input } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'cs-section-card',
  imports: [MatCardModule, MatIconModule],
  templateUrl: './section-card.html',
  styleUrl: './section-card.scss',
})
export class SectionCard {
  readonly heading = input<string>();
  readonly icon = input<string>();
  readonly dense = input(false);
}
