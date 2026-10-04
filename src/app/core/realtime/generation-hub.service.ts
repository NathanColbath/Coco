import { Injectable, inject, signal } from '@angular/core';
import { Subject } from 'rxjs';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { AuthService } from '../auth/auth.service';

export interface GenerationStarted {
  generationId: string;
  model: string;
}

export interface GenerationToken {
  generationId: string;
  text: string;
}

export interface GenerationCompleted {
  generationId: string;
  block: { id: string; parentId: string | null; text: string };
  durationMs: number;
}

export interface GenerationFailed {
  generationId: string;
  message: string;
}

export interface ImageJobEvent {
  generationId: string;
  panelId?: string | null;
  targetId?: string | null;
  imageId?: string;
  kind?: string;
  model?: string;
  width?: number;
  height?: number;
  costAnlas?: number | null;
  wasFree?: boolean;
  durationMs?: number;
  message?: string;
}

export interface CurationProgress {
  projectId: string;
  stage: string;
  message: string;
  percent: number;
  failed?: boolean;
}

const EVENTS = {
  started: 'generation.started',
  token: 'generation.token',
  completed: 'generation.completed',
  failed: 'generation.failed',
  imageStarted: 'image.started',
  imageCompleted: 'image.completed',
  imageFailed: 'image.failed',
  curationProgress: 'curation.progress',
} as const;

/**
 * Wraps the SignalR connection that streams generation progress. Connects
 * lazily and reconnects automatically; events are scoped to the signed-in user
 * by the server's group membership.
 */
@Injectable({ providedIn: 'root' })
export class GenerationHubService {
  private readonly auth = inject(AuthService);
  private connection: HubConnection | null = null;
  private connecting: Promise<void> | null = null;

  readonly connected = signal(false);

  readonly started$ = new Subject<GenerationStarted>();
  readonly token$ = new Subject<GenerationToken>();
  readonly completed$ = new Subject<GenerationCompleted>();
  readonly failed$ = new Subject<GenerationFailed>();
  readonly imageStarted$ = new Subject<ImageJobEvent>();
  readonly imageCompleted$ = new Subject<ImageJobEvent>();
  readonly imageFailed$ = new Subject<ImageJobEvent>();
  readonly curationProgress$ = new Subject<CurationProgress>();

  async ensureConnected(): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) {
      return;
    }

    this.connecting ??= this.connect();
    await this.connecting;
    this.connecting = null;
  }

  async disconnect(): Promise<void> {
    const connection = this.connection;
    this.connection = null;
    this.connected.set(false);
    if (connection) {
      await connection.stop();
    }
  }

  private async connect(): Promise<void> {
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/generation', {
        accessTokenFactory: () => this.auth.getAccessToken() ?? '',
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on(EVENTS.started, (payload: GenerationStarted) =>
      this.started$.next(payload),
    );
    connection.on(EVENTS.token, (payload: GenerationToken) => this.token$.next(payload));
    connection.on(EVENTS.completed, (payload: GenerationCompleted) =>
      this.completed$.next(payload),
    );
    connection.on(EVENTS.failed, (payload: GenerationFailed) => this.failed$.next(payload));
    connection.on(EVENTS.imageStarted, (payload: ImageJobEvent) =>
      this.imageStarted$.next(payload),
    );
    connection.on(EVENTS.imageCompleted, (payload: ImageJobEvent) =>
      this.imageCompleted$.next(payload),
    );
    connection.on(EVENTS.imageFailed, (payload: ImageJobEvent) =>
      this.imageFailed$.next(payload),
    );
    connection.on(EVENTS.curationProgress, (payload: CurationProgress) =>
      this.curationProgress$.next(payload),
    );

    connection.onreconnected(() => this.connected.set(true));
    connection.onclose(() => this.connected.set(false));

    await connection.start();
    this.connection = connection;
    this.connected.set(true);
  }
}
