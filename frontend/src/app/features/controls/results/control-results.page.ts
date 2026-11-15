import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { Router, RouterLink } from '@angular/router';
import { debounceTime, distinctUntilChanged, skip } from 'rxjs';
import { DataTable } from '../../../shared/components/data-table/data-table';
import { DefinitionDescription } from '../../../shared/components/definition-description/definition-description';
import { LoadingBar } from '../../../shared/components/loading-bar/loading-bar';
import { EmptyState } from '../../../shared/components/empty-state/empty-state';
import { PageHeader } from '../../../shared/components/page-header/page-header';
import { ResultsStore } from './results.store';
import { inputValue } from '../../../core/utils/dom';
import { CatalogStore } from '../../../core/services/catalog.store';

const SEARCH_DEBOUNCE_MS = 400;

/** A control's violations: summary numbers, what the control means, and the records with a reason each. */
@Component({
  selector: 'app-control-results-page',
  imports: [
    DatePipe,
    DecimalPipe,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    LoadingBar,
    PageHeader,
    EmptyState,
    DefinitionDescription,
    DataTable,
  ],
  providers: [ResultsStore],
  templateUrl: './control-results.page.html',
  styleUrl: './control-results.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ControlResultsPage implements OnInit {
  readonly id = input.required<string>();
  /** `?run=true` (from the list's "הרצה" button) runs the control on arrival. */
  readonly run = input<string>();

  protected readonly store = inject(ResultsStore);
  private readonly catalog = inject(CatalogStore);
  private readonly router = inject(Router);

  protected readonly search = signal('');
  /** The server's limit of search / filter text, from the catalog (loaded once for the whole app). */
  protected readonly maxSearch = computed(() => this.catalog.limits()?.maxSearchLength ?? null);
  protected readonly title = computed(() => this.store.control()?.name ?? this.store.result()?.controlName ?? 'תוצאות בקרה');
  /** The user's own business wording first; the generated sentence below it states exactly what runs. */
  protected readonly subtitle = computed(() => {
    const result = this.store.result();
    return this.store.control()?.description ?? (result ? `בדיקת ${result.rootEntityName}` : null);
  });

  constructor() {
    toObservable(this.search)
      .pipe(skip(1), debounceTime(SEARCH_DEBOUNCE_MS), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe((search) => this.store.setSearch(search));
  }

  ngOnInit(): void {
    void this.catalog.load();
    const runNow = this.run() === 'true';
    void this.store.open(Number(this.id()), runNow);
    if (runNow) {
      // Refreshing the page must not run the control again.
      void this.router.navigate([], { queryParams: { run: null }, queryParamsHandling: 'merge', replaceUrl: true });
    }
  }

  protected setSearch(event: Event): void {
    this.search.set(inputValue(event));
  }
}
