import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import { ApiError } from '../../../core/api/api-error';
import { ControlsApi } from '../../../core/api/controls-api.service';
import { ColumnFilter, Control, ResultsPage, ResultsRequest, RunResult } from '../../../core/models/control.models';

const DEFAULT_PAGE_SIZE = 25;

/**
 * State of the results screen (provided per page). Every change of page / search / sort / filters re-reads the
 * results from the server; only the LATEST request may update the screen, so a slow older response never
 * overwrites a newer one. The summary (violations, records checked, run time) comes with a run or the first read;
 * browsing keeps it and does not ask the server to count again.
 */
@Injectable()
export class ResultsStore {
  private readonly api = inject(ControlsApi);
  private controlId = 0;
  private latest = 0;

  private readonly _control = signal<Control | null>(null);
  private readonly _result = signal<RunResult | null>(null);
  private readonly _request = signal<ResultsRequest>({ page: 1, pageSize: DEFAULT_PAGE_SIZE, search: '', filters: [] });
  private readonly _loading = signal(false);
  private readonly _running = signal(false);
  private readonly _error = signal<string | null>(null);
  private readonly _notFound = signal(false);

  readonly control = this._control.asReadonly();
  readonly result = this._result.asReadonly();
  readonly request = this._request.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly running = this._running.asReadonly();
  readonly error = this._error.asReadonly();
  /** The control does not exist (deleted, or a wrong address) - nothing to run or edit. */
  readonly notFound = this._notFound.asReadonly();

  readonly isFiltered = computed(() => !!this._request().search?.trim() || (this._request().filters?.length ?? 0) > 0);
  readonly violationRate = computed(() => {
    const result = this._result();
    return result && result.totalScanned > 0 ? (result.violationCount / result.totalScanned) * 100 : 0;
  });

  /** Opens a control's results: runs it now (recording the run) or just reads the current results. */
  async open(controlId: number, runNow: boolean): Promise<void> {
    this.controlId = controlId;
    this.api.get(controlId).subscribe({ next: (control) => this._control.set(control), error: () => undefined });
    await (runNow ? this.run() : this.load());
  }

  /**
   * Runs the control now; the run is recorded on the server (last-run result in the list).
   * The run returns the default first page, so a user's search / filters / sort / page size are re-applied after it.
   */
  async run(): Promise<void> {
    this._running.set(true);
    this._request.update((r) => ({ ...r, page: 1 }));
    try {
      await this.execute(this.api.run(this.controlId));
      if (this.hasCustomView() && !this._error()) {
        await this.load();
      }
    } finally {
      this._running.set(false);
    }
  }

  setPage(page: number, pageSize: number): void {
    this.change({ page, pageSize });
  }

  setSearch(search: string): void {
    this.change({ search, page: 1 });
  }

  setSort(sortKey: string | null, sortDescending: boolean): void {
    this.change({ sortKey, sortDescending, page: 1 });
  }

  setFilters(filters: ColumnFilter[]): void {
    this.change({ filters, page: 1 });
  }

  private hasCustomView(): boolean {
    const request = this._request();
    return this.isFiltered() || !!request.sortKey || request.pageSize !== DEFAULT_PAGE_SIZE;
  }

  private change(patch: Partial<ResultsRequest>): void {
    this._request.update((r) => ({ ...r, ...patch }));
    void this.load();
  }

  private load(): Promise<void> {
    // Counted only while the screen has no summary yet (e.g. the first read was superseded by a search).
    return this.execute(this.api.results(this.controlId, { ...this._request(), includeSummary: this._result() === null }));
  }

  private async execute(call: Observable<RunResult | ResultsPage>): Promise<void> {
    const ticket = ++this.latest;
    this._loading.set(true);
    try {
      const page = await firstValueFrom(call);
      if (ticket === this.latest) {
        this._result.set(this.withSummary(page));
        this._error.set(null);
      }
    } catch (error) {
      if (ticket === this.latest) {
        this._error.set(error instanceof ApiError ? error.message : 'טעינת התוצאות נכשלה.');
        this._notFound.set(error instanceof ApiError && error.status === 404);
      }
    } finally {
      if (ticket === this.latest) {
        this._loading.set(false);
      }
    }
  }

  /** A page without the summary keeps the one shown (from the run or the first read). */
  private withSummary(page: RunResult | ResultsPage): RunResult {
    const shown = this._result();
    if (page.violationCount !== null && page.totalScanned !== null) {
      return { ...page, violationCount: page.violationCount, totalScanned: page.totalScanned };
    }
    if (!shown) {
      throw new Error('A results page without its summary arrived before any summary.');
    }
    const { violationCount, totalScanned, durationMs, executedAt } = shown;
    return { ...page, violationCount, totalScanned, durationMs, executedAt };
  }
}
