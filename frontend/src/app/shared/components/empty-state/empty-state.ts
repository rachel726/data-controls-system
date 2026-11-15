import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

/** Friendly "nothing here yet" panel with an optional call to action (projected). */
@Component({
  selector: 'app-empty-state',
  imports: [MatIconModule],
  template: `
    <section class="empty-state" role="status">
      <mat-icon class="icon" aria-hidden="true">{{ icon() }}</mat-icon>
      <h2>{{ title() }}</h2>
      @if (message()) {
        <p>{{ message() }}</p>
      }
      <ng-content />
    </section>
  `,
  styles: `
    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      text-align: center;
      gap: 8px;
      padding: 48px 16px;
      color: var(--mat-sys-on-surface-variant);
    }
    .icon {
      font-size: 48px;
      width: 48px;
      height: 48px;
      color: var(--mat-sys-primary);
    }
    h2 {
      font: var(--mat-sys-title-large);
      color: var(--mat-sys-on-surface);
    }
    p {
      margin: 0 0 8px;
      max-width: 48ch;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EmptyState {
  readonly icon = input('inbox');
  readonly title = input.required<string>();
  readonly message = input<string | null>(null);
}
