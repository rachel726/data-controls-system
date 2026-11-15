import { BreakpointObserver } from '@angular/cdk/layout';
import { ChangeDetectionStrategy, Component, Injectable, computed, inject, input, output } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorIntl, MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSortModule, Sort } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { map } from 'rxjs';
import { ColumnFilter, ResultColumn, ResultRow } from '../../../core/models/control.models';
import { inputValue } from '../../../core/utils/dom';

export interface PageChange {
  readonly page: number;
  readonly pageSize: number;
}

export interface SortChange {
  readonly key: string | null;
  readonly descending: boolean;
}

/** Hebrew wording for the paginator. */
@Injectable()
export class HebrewPaginatorIntl extends MatPaginatorIntl {
  override itemsPerPageLabel = 'שורות בעמוד:';
  override nextPageLabel = 'העמוד הבא';
  override previousPageLabel = 'העמוד הקודם';
  override firstPageLabel = 'העמוד הראשון';
  override lastPageLabel = 'העמוד האחרון';
  override getRangeLabel = (page: number, pageSize: number, length: number): string => {
    if (length === 0) {
      return '0 מתוך 0';
    }
    const start = page * pageSize + 1;
    return `${start}–${Math.min(start + pageSize - 1, length)} מתוך ${length}`;
  };
}

/** Below this width a row is a card: the reasons come first instead of after a horizontal scroll. */
const MOBILE_QUERY = '(max-width: 959.98px)';
const REASONS = '__reasons';

/**
 * Generic server-side results table: the columns come from the server (business names, types), so it works
 * for any entity. Paging, sorting and per-column filters are emitted - the caller fetches the data.
 * Shows the reason(s) of each record; on phones the rows become cards.
 * States never change the page's geometry: "no rows" is shown inside the table, loading only dims the rows.
 */
@Component({
  selector: 'app-data-table',
  imports: [MatTableModule, MatSortModule, MatPaginatorModule, MatIconModule],
  providers: [{ provide: MatPaginatorIntl, useClass: HebrewPaginatorIntl }],
  templateUrl: './data-table.html',
  styleUrl: './data-table.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DataTable {
  readonly columns = input.required<readonly ResultColumn[]>();
  readonly rows = input.required<readonly ResultRow[]>();
  /** Rows matching the current search / filters (null when unknown). */
  readonly total = input<number | null>(null);
  readonly page = input(1);
  readonly pageSize = input(25);
  readonly sortKey = input<string | null>(null);
  readonly sortDescending = input(false);
  readonly filters = input<readonly ColumnFilter[]>([]);
  /** New data is on its way: the current rows stay (dimmed) so the table keeps its size instead of jumping. */
  readonly loading = input(false);
  /** Shown in place of the rows when there are none, e.g. nothing matches the search. */
  readonly emptyMessage = input('אין נתונים להצגה.');
  /** The longest text a column filter accepts (the server's limit); null = not limited here. */
  readonly maxFilterLength = input<number | null>(null);

  readonly pageChange = output<PageChange>();
  readonly sortChange = output<SortChange>();
  readonly filtersChange = output<ColumnFilter[]>();

  protected readonly inputValue = inputValue;
  protected readonly reasonsColumn = REASONS;
  protected readonly pageSizes = [10, 25, 50, 100];

  private readonly breakpoints = inject(BreakpointObserver);
  protected readonly isMobile = toSignal(this.breakpoints.observe(MOBILE_QUERY).pipe(map((s) => s.matches)), {
    initialValue: this.breakpoints.isMatched(MOBILE_QUERY),
  });

  protected readonly displayedColumns = computed(() => [...this.columns().map((c) => c.key), REASONS]);
  protected readonly filterColumns = computed(() => this.displayedColumns().map((key) => `filter:${key}`));
  /** In cards, the fields the control checks come first. */
  protected readonly cardColumns = computed(() =>
    [...this.columns()].sort((a, b) => Number(b.isConditionField) - Number(a.isConditionField)),
  );

  protected filterValue(key: string): string {
    return this.filters().find((f) => f.key === key)?.value ?? '';
  }

  protected setFilter(key: string, value: string): void {
    const others = this.filters().filter((f) => f.key !== key);
    const trimmed = value.trim();
    this.filtersChange.emit(trimmed ? [...others, { key, value: trimmed }] : others);
  }

  protected onSort(sort: Sort): void {
    this.sortChange.emit({ key: sort.direction ? sort.active : null, descending: sort.direction === 'desc' });
  }

  protected onPage(event: PageEvent): void {
    this.pageChange.emit({ page: event.pageIndex + 1, pageSize: event.pageSize });
  }
}
