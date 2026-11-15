import { EnvironmentInjector, Injectable, inject } from '@angular/core';
import type { MatSnackBar } from '@angular/material/snack-bar';

/**
 * Short Hebrew messages at the bottom of the screen.
 * The snack-bar (and its overlay layer) is loaded on first use, keeping it out of the initial bundle:
 * the global error interceptor depends on this service, but most sessions never show a message.
 */
@Injectable({ providedIn: 'root' })
export class NotifyService {
  private readonly injector = inject(EnvironmentInjector);
  private snackBar: Promise<MatSnackBar> | null = null;

  success(message: string): void {
    void this.open(message, 3500, 'notify-success');
  }

  error(message: string): void {
    void this.open(message, 8000, 'notify-error');
  }

  private async open(message: string, duration: number, panelClass: string): Promise<void> {
    this.snackBar ??= import('@angular/material/snack-bar').then((m) => this.injector.get(m.MatSnackBar));
    (await this.snackBar).open(message, 'סגירה', { duration, direction: 'rtl', panelClass });
  }
}
