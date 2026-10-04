import {
  Directive,
  ElementRef,
  Injectable,
  effect,
  inject,
  input,
  OnDestroy,
} from '@angular/core';
import { getApiAuthToken } from '../../../core/api/api';

/**
 * Fetches authenticated images and caches the object URL.
 *
 * Stored images are served from an endpoint that requires the session token,
 * and a plain `<img src>` sends no headers — which shows up as a broken image
 * with alt text. Assets are content addressed, so the URLs never go stale.
 */
@Injectable({ providedIn: 'root' })
export class AuthedImageLoader {
  private readonly cache = new Map<string, Promise<string | null>>();

  load(assetId: string): Promise<string | null> {
    let pending = this.cache.get(assetId);

    if (!pending) {
      pending = this.fetch(assetId);
      this.cache.set(assetId, pending);
    }

    return pending;
  }

  private async fetch(assetId: string): Promise<string | null> {
    const token = getApiAuthToken();

    try {
      const response = await fetch(`/api/images/${assetId}`, {
        headers: token ? { Authorization: `Bearer ${token}` } : {},
      });

      if (!response.ok) {
        return null;
      }

      return URL.createObjectURL(await response.blob());
    } catch {
      return null;
    }
  }
}

/** `<img [csAuthedImage]="assetId" />` */
@Directive({ selector: 'img[csAuthedImage]' })
export class AuthedImage implements OnDestroy {
  private readonly element = inject<ElementRef<HTMLImageElement>>(ElementRef);
  private readonly loader = inject(AuthedImageLoader);
  private objectUrl: string | null = null;

  readonly assetId = input<string | null | undefined>(null, { alias: 'csAuthedImage' });

  constructor() {
    effect(() => {
      const id = this.assetId();
      const image = this.element.nativeElement;

      if (!id) {
        image.removeAttribute('src');
        return;
      }

      void this.loader.load(id).then((url) => {
        // The input may have changed while this was in flight.
        if (url && this.assetId() === id) {
          image.src = url;
        }
      });
    });
  }

  ngOnDestroy(): void {
    if (this.objectUrl) {
      URL.revokeObjectURL(this.objectUrl);
    }
  }
}
