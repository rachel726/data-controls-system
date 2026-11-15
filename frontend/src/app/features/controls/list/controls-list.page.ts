import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { catchError, debounceTime, distinctUntilChanged, of, startWith, switchMap, tap } from 'rxjs';
import { ControlsApi } from '../../../core/api/controls-api.service';
import { ControlListItem } from '../../../core/models/control.models';
import { NotifyService } from '../../../core/services/notify.service';
import { ConfirmService } from '../../../shared/components/confirm-dialog/confirm-dialog';
import { LoadingBar } from '../../../shared/components/loading-bar/loading-bar';
import { EmptyState } from '../../../shared/components/empty-state/empty-state';
import { PageHeader } from '../../../shared/components/page-header/page-header';
import { inputValue } from '../../../core/utils/dom';

const SEARCH_DEBOUNCE_MS = 300;

/** The saved controls: search, run, edit and delete, with the outcome of each control's last run. */
@Component({
  selector: 'app-controls-list-page',
  imports: [
    DatePipe,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    LoadingBar,
    MatTooltipModule,
    PageHeader,
    EmptyState,
  ],
  templateUrl: './controls-list.page.html',
  styleUrl: './controls-list.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ControlsListPage {
  private readonly api = inject(ControlsApi);
  private readonly router = inject(Router);
  private readonly confirm = inject(ConfirmService);
  private readonly notify = inject(NotifyService);

  protected readonly inputValue = inputValue;
  protected readonly search = signal('');
  protected readonly controls = signal<readonly ControlListItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  private readonly reload = signal(0);

  /** Always a line (never null), so the header keeps its height while loading. */
  protected readonly subtitle = computed(() => {
    const count = this.controls().length;
    if (this.loading() && count === 0) {
      return 'טוען…';
    }
    return count === 1 ? 'בקרה אחת' : `${count} בקרות`;
  });

  constructor() {
    // Search is server-side and debounced: no request per keystroke, stale responses dropped (switchMap).
    toObservable(computed(() => ({ search: this.search().trim(), reload: this.reload() })))
      .pipe(
        debounceTime(SEARCH_DEBOUNCE_MS),
        distinctUntilChanged((a, b) => a.search === b.search && a.reload === b.reload),
        tap(() => this.loading.set(true)),
        switchMap(({ search }) =>
          this.api.list(search).pipe(
            tap(() => this.failed.set(false)),
            catchError(() => {
              this.failed.set(true);
              return of(null);
            }),
          ),
        ),
        startWith(null),
        takeUntilDestroyed(),
      )
      .subscribe((controls) => {
        if (controls) {
          this.controls.set(controls);
        }
        if (controls || this.failed()) {
          this.loading.set(false);
        }
      });
  }

  protected retry(): void {
    this.reload.update((n) => n + 1);
  }

  protected clearSearch(): void {
    this.search.set('');
  }

  protected run(control: ControlListItem): void {
    void this.router.navigate(['/controls', control.id, 'results'], { queryParams: { run: true } });
  }

  protected async delete(control: ControlListItem): Promise<void> {
    const confirmed = await this.confirm.ask({
      title: 'מחיקת בקרה',
      message: `למחוק את הבקרה "${control.name}"? לא ניתן לבטל פעולה זו.`,
      confirmLabel: 'מחיקה',
      danger: true,
    });
    if (!confirmed) {
      return;
    }

    this.api.delete(control.id).subscribe(() => {
      this.controls.update((list) => list.filter((c) => c.id !== control.id));
      this.notify.success(`הבקרה "${control.name}" נמחקה.`);
    });
  }
}
