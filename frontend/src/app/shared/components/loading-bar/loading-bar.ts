import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatProgressBarModule } from '@angular/material/progress-bar';

/**
 * A progress bar that never moves the page: it always takes the same 4px slot, and only the bar inside appears
 * while `active`. Inserting a bar into the flow on every search / filter / run pushes the content down and back
 * up - this component exists so no screen does that.
 */
@Component({
  selector: 'app-loading-bar',
  imports: [MatProgressBarModule],
  template: `
    @if (active()) {
      <mat-progress-bar mode="indeterminate" [attr.aria-label]="label()" />
    }
  `,
  styles: `
    :host {
      display: block;
      height: 4px;
      overflow: hidden;
    }
  `,
  host: { role: 'status', '[attr.aria-busy]': 'active()' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoadingBar {
  readonly active = input(false);
  readonly label = input('טוען');
}
