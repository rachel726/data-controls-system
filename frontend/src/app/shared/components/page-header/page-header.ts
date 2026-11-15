import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Page title with an optional subtitle; action buttons are projected (placed at the end side in RTL). */
@Component({
  selector: 'app-page-header',
  template: `
    <header class="page-header">
      <div class="text">
        <h1>{{ title() }}</h1>
        @if (subtitle()) {
          <p class="subtitle">{{ subtitle() }}</p>
        }
      </div>
      <div class="actions"><ng-content /></div>
    </header>
  `,
  styles: `
    .page-header {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 12px 24px;
      margin-block-end: 24px;
    }
    h1 {
      font: var(--mat-sys-headline-small);
    }
    .subtitle {
      margin: 4px 0 0;
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-medium);
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PageHeader {
  readonly title = input.required<string>();
  readonly subtitle = input<string | null>(null);
}
