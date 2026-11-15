import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { LoadingBar } from '../loading-bar/loading-bar';
import { DefinitionError } from '../../../core/models/control.models';

/**
 * Shows what a control means, in the server's Hebrew wording:
 * "רשומה תיחשב חריגה כאשר: גיל קטן מ-18 וגם ..." - or the reasons it is not valid yet.
 */
@Component({
  selector: 'app-definition-description',
  imports: [MatIconModule, LoadingBar],
  template: `
    <section class="description" [class.invalid]="errors().length > 0 || invalid()" aria-live="polite">
      <h3>רשומה תיחשב חריגה כאשר:</h3>
      <!-- over the panel's top edge: checking never moves the sentence -->
      <app-loading-bar class="checking" [active]="loading()" label="בודק את הבקרה" />
      @if (errors().length > 0) {
        <ul class="errors">
          @for (error of errors(); track error.path + error.code) {
            <li><mat-icon aria-hidden="true">error</mat-icon>{{ error.message }}</li>
          }
        </ul>
      } @else if (sentence()) {
        <p class="sentence">{{ sentence() }}</p>
      } @else if (!loading()) {
        <p class="placeholder" [class.error]="invalid()">
          @if (invalid()) {
            <mat-icon aria-hidden="true">error</mat-icon>
          }
          {{ placeholder() }}
        </p>
      }
    </section>
  `,
  styles: `
    .description {
      position: relative;
      overflow: hidden;
      padding: 16px;
      border-radius: 12px;
      background: var(--mat-sys-surface-container);
      border-inline-start: 4px solid var(--mat-sys-primary);
    }
    .checking {
      position: absolute;
      inset-block-start: 0;
      inset-inline: 0;
    }
    .description.invalid {
      border-inline-start-color: var(--mat-sys-error);
    }
    h3 {
      font: var(--mat-sys-title-small);
      color: var(--mat-sys-on-surface-variant);
      margin-block-end: 8px;
    }
    .sentence {
      margin: 0;
      font: var(--mat-sys-body-large);
    }
    .placeholder {
      margin: 0;
      color: var(--mat-sys-on-surface-variant);
    }
    .placeholder.error {
      display: flex;
      align-items: flex-start;
      gap: 6px;
      color: var(--mat-sys-error);
    }
    .placeholder.error mat-icon {
      font-size: 20px;
      width: 20px;
      height: 20px;
      flex: none;
    }
    .errors {
      margin: 0;
      padding: 0;
      list-style: none;
      display: grid;
      gap: 6px;
      color: var(--mat-sys-error);
    }
    .errors li {
      display: flex;
      align-items: flex-start;
      gap: 6px;
    }
    .errors mat-icon {
      font-size: 20px;
      width: 20px;
      height: 20px;
      flex: none;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DefinitionDescription {
  readonly sentence = input<string | null>(null);
  readonly errors = input<readonly DefinitionError[]>([]);
  readonly loading = input(false);
  /** The control has mistakes shown elsewhere (on its conditions): the placeholder is then a short note in red. */
  readonly invalid = input(false);
  readonly placeholder = input('הוסיפו תנאי כדי לראות כאן את משמעות הבקרה.');
}
