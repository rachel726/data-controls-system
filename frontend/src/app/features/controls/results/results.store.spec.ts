import { TestBed } from '@angular/core/testing';
import { Observable, Subject, of, throwError } from 'rxjs';
import { ApiError } from '../../../core/api/api-error';
import { ControlsApi } from '../../../core/api/controls-api.service';
import { ResultsPage, ResultsRequest, RunResult } from '../../../core/models/control.models';
import { ResultsStore } from './results.store';

function result(patch: Partial<RunResult> = {}): RunResult {
  return {
    controlId: 1, controlName: 'קטינים', rootEntityName: 'עובדים', executedAt: '2026-11-12T10:00:00Z', durationMs: 12,
    violationCount: 3, totalScanned: 222, totalFiltered: 3, page: 1, pageSize: 25, columns: [], rows: [], ...patch,
  };
}

/** A browsing page: the server did not count again. */
function pageOnly(patch: Partial<ResultsPage> = {}): ResultsPage {
  return { ...result(), violationCount: null, totalScanned: null, ...patch };
}

describe('ResultsStore', () => {
  let store: ResultsStore;
  let runs: number;
  let requests: ResultsRequest[];
  let runResponse: () => Observable<RunResult>;
  let resultsResponse: (request: ResultsRequest) => Observable<ResultsPage>;

  beforeEach(() => {
    runs = 0;
    requests = [];
    runResponse = () => of(result());
    resultsResponse = () => of(result());
    TestBed.configureTestingModule({
      providers: [
        ResultsStore,
        {
          provide: ControlsApi,
          useValue: {
            get: () => of({ name: 'קטינים', definitionDescription: 'גיל קטן מ-18' }),
            run: () => {
              runs++;
              return runResponse();
            },
            results: (_: number, request: ResultsRequest) => {
              requests.push(request);
              return resultsResponse(request);
            },
          },
        },
      ],
    });
    store = TestBed.inject(ResultsStore);
  });

  it('runs the control on arrival from "הרצה", otherwise only reads the results', async () => {
    await store.open(1, true);
    expect([runs, requests.length]).toEqual([1, 0]);
    expect(store.result()?.violationCount).toBe(3);
    expect(store.violationRate()).toBeCloseTo(1.35, 2);

    await store.open(1, false);
    expect([runs, requests.length]).toEqual([1, 1]);
  });

  it('counts on the first read only; browsing keeps the summary shown', async () => {
    await store.open(1, false);
    expect(requests[0].includeSummary).toBe(true);

    resultsResponse = () => of(pageOnly({ totalFiltered: 1, durationMs: 4, executedAt: '2026-11-12T10:05:00Z' }));
    store.setSearch('כהן');
    await Promise.resolve();

    expect(requests.at(-1)?.includeSummary).toBe(false);
    expect(store.result()).toMatchObject({
      totalFiltered: 1, violationCount: 3, totalScanned: 222, durationMs: 12, executedAt: '2026-11-12T10:00:00Z',
    });
  });

  it('a first read replaced by a search still asks for the summary', async () => {
    const first = new Subject<ResultsPage>();
    resultsResponse = () => first;
    void store.open(1, false);

    resultsResponse = () => of(result({ totalFiltered: 1 }));
    store.setSearch('כהן');   // before the first read answered: nothing to keep yet
    await Promise.resolve();

    expect(requests.map((r) => r.includeSummary)).toEqual([true, true]);
    expect(store.result()?.violationCount).toBe(3);
  });

  it('"run again" brings a new summary, which browsing then keeps', async () => {
    await store.open(1, false);
    runResponse = () => of(result({ violationCount: 4, totalFiltered: 4 }));
    await store.run();

    resultsResponse = () => of(pageOnly({ totalFiltered: 4, page: 2 }));
    store.setPage(2, 25);
    await Promise.resolve();

    expect(store.result()).toMatchObject({ page: 2, violationCount: 4 });
  });

  it('only the latest request may update the screen', async () => {
    await store.open(1, false);
    const slow = new Subject<ResultsPage>();
    const fast = new Subject<ResultsPage>();
    resultsResponse = (request) => (request.search === 'כ' ? slow : fast);

    store.setSearch('כ');      // older, slow
    store.setSearch('כהן');    // newer, fast
    fast.next(result({ totalFiltered: 1 }));
    fast.complete();
    await Promise.resolve();
    slow.next(result({ totalFiltered: 99 }));
    slow.complete();
    await Promise.resolve();

    expect(store.result()?.totalFiltered).toBe(1);
    expect(store.loading()).toBe(false);
  });

  it('paging, sorting, filtering and searching go back to page 1 where it matters', async () => {
    await store.open(1, false);

    store.setPage(3, 50);
    expect(requests.at(-1)).toMatchObject({ page: 3, pageSize: 50 });
    store.setSort('salary', true);
    expect(requests.at(-1)).toMatchObject({ page: 1, sortKey: 'salary', sortDescending: true });
    store.setFilters([{ key: 'isActive', value: 'true' }]);
    expect(requests.at(-1)).toMatchObject({ page: 1, filters: [{ key: 'isActive', value: 'true' }] });
    expect(store.isFiltered()).toBe(true);
  });

  it('"run again" keeps the user\'s search, filters and sort', async () => {
    // Regression: the run returns the default first page, which did not match the search box.
    await store.open(1, false);
    store.setSearch('כהן');
    const before = requests.length;

    await store.run();

    expect(runs).toBe(1);
    expect(requests.length).toBe(before + 1);
    expect(requests.at(-1)).toMatchObject({ page: 1, search: 'כהן' });
  });

  it('a plain "run again" does not read the results twice', async () => {
    await store.open(1, false);
    const before = requests.length;

    await store.run();

    expect(requests.length).toBe(before);
  });

  it('keeps the Hebrew reason when the run fails', async () => {
    runResponse = () => throwError(() => new ApiError(504, 'הרצת הבקרה ארכה זמן רב מדי.', 'הרצת הבקרה ארכה יותר מ-30 שניות והופסקה.'));

    await store.open(1, true);

    expect(store.error()).toBe('הרצת הבקרה ארכה יותר מ-30 שניות והופסקה.');
    expect(store.result()).toBeNull();
    expect(store.running()).toBe(false);
    expect(store.notFound()).toBe(false);
  });

  it('tells a control that does not exist from a failed run', async () => {
    runResponse = () => throwError(() => new ApiError(404, 'לא נמצא.', 'הבקרה המבוקשת (מספר 999) לא נמצאה.'));

    await store.open(999, true);

    expect(store.notFound()).toBe(true);
    expect(store.error()).toBe('הבקרה המבוקשת (מספר 999) לא נמצאה.');
  });
});
