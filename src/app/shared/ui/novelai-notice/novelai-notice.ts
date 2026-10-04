import { Component, computed, inject } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../../../core/auth/auth.service';

/**
 * Explains that NovelAI's API needs a paid subscription.
 *
 * The free trial is web-only — its reCAPTCHA is minted by NovelAI's own site —
 * so a trial token connects fine and then fails every generation with a
 * reCAPTCHA error. Renders nothing when the account can generate.
 */
@Component({
  selector: 'cs-novelai-notice',
  imports: [MatIconModule],
  template: `
    @if (message(); as text) {
      <p class="cs-notice">
        <mat-icon aria-hidden="true">info</mat-icon>
        <span>{{ text }}</span>
      </p>
    }
  `,
  styles: `
    .cs-notice {
      display: flex;
      align-items: flex-start;
      gap: 8px;
      margin: 0;
      padding: 10px 14px;
      border-radius: var(--cs-radius-sm);
      background-color: var(--cs-warning-container);
      color: var(--cs-on-warning-container);
      font: var(--mat-sys-body-small);
    }

    mat-icon {
      font-size: 18px;
      width: 18px;
      height: 18px;
      flex: 0 0 auto;
    }
  `,
})
export class NovelAiNotice {
  private readonly auth = inject(AuthService);

  protected readonly message = computed(() => {
    const user = this.auth.user();
    if (!user) {
      return null;
    }

    if (!user.novelAiConnected) {
      return 'NovelAI is not connected yet. Add a persistent token in Settings to generate.';
    }

    if (!user.novelAiSubscriptionActive) {
      return (
        'This NovelAI account has no active subscription, so the API will not generate. ' +
        'The free trial only works on novelai.net — its reCAPTCHA cannot run here. ' +
        'Subscribe to Tablet, Scroll or Opus, then refresh the account in Settings.'
      );
    }

    return null;
  });
}
