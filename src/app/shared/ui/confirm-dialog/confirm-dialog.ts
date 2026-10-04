import { Component, Injectable, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import {
  MAT_DIALOG_DATA,
  MatDialog,
  MatDialogModule,
  MatDialogRef,
} from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';

export interface ConfirmDialogData {
  title: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  danger?: boolean;
  /** Optional list of what will be destroyed, e.g. "12 pages · 47 images · 312 MB". */
  impacts?: Array<{ label: string; detail?: string }>;
  /** When true the confirm button stays disabled until the user ticks a checkbox. */
  requireAcknowledgement?: boolean;
}

@Component({
  selector: 'cs-confirm-dialog',
  imports: [MatButtonModule, MatCheckboxModule, MatDialogModule],
  templateUrl: './confirm-dialog.html',
  styleUrl: './confirm-dialog.scss',
})
export class ConfirmDialog {
  protected readonly data = inject<ConfirmDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject<MatDialogRef<ConfirmDialog, boolean>>(MatDialogRef);

  protected readonly acknowledged = signal(false);
  protected readonly confirmLabel = computed(() => this.data.confirmLabel ?? 'Delete');
  protected readonly cancelLabel = computed(() => this.data.cancelLabel ?? 'Cancel');
  protected readonly danger = computed(() => this.data.danger ?? true);
  protected readonly impacts = computed(() => this.data.impacts ?? []);
  protected readonly canConfirm = computed(
    () => !this.data.requireAcknowledgement || this.acknowledged(),
  );

  protected confirm(): void {
    this.dialogRef.close(true);
  }

  protected cancel(): void {
    this.dialogRef.close(false);
  }
}

@Injectable({ providedIn: 'root' })
export class ConfirmDialogService {
  private readonly dialog = inject(MatDialog);

  /** Resolves true only when the user confirms. */
  async confirm(data: ConfirmDialogData): Promise<boolean> {
    const dialogRef = this.dialog.open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, {
      width: '480px',
      data,
    });

    return (await firstValueFrom(dialogRef.afterClosed())) === true;
  }
}
